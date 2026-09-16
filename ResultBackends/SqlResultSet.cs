using System.Data.Common;
using System.Data;

namespace Scarab.ResultBackends;

public class SqlResultSet : IResultSet
{
    private readonly string _jobId;
    private readonly string _taskName;
    private readonly string _tableName;
    private readonly SqlResultBackend _backend;
    private readonly DbConnection _connection;
    private readonly DbTransaction _transaction;
    private InsertSchema? _schema;
    private DbCommand? _insertCmd;
    private DbColumn[]? _columnTypes;

    public SqlResultSet(string jobId, string taskName, string tableName, SqlResultBackend backend, DbConnection connection, DbTransaction transaction)
    {
        _jobId = jobId;
        _taskName = taskName;
        _tableName = tableName;
        _backend = backend;
        _connection = connection;
        _transaction = transaction;
    }

    public Task RegisterColTypes(string[] columnNames, DbColumn[] columnTypes)
    {
        _columnTypes = columnTypes;
        _schema = _backend.BuildSchema(_taskName, columnNames, columnTypes);
        return Task.CompletedTask;
    }

    public bool IsColTypesRegistered() => _backend.SchemaCache.ContainsKey(_taskName);

    public async Task WriteCols(string[] columnNames)
    {
        _schema ??= _backend.SchemaCache.GetValueOrDefault(_taskName);
        if (_schema == null)
            throw new InvalidOperationException($"Schema not registered for task '{_taskName}'");

        await using var dropCmd = _connection.CreateCommand();
        dropCmd.Transaction = _transaction;
        dropCmd.CommandText = string.Format(_schema.DropTable, _tableName);
        await dropCmd.ExecuteNonQueryAsync();

        await using var createCmd = _connection.CreateCommand();
        createCmd.Transaction = _transaction;
        createCmd.CommandText = string.Format(_schema.CreateTable, _tableName);
        await createCmd.ExecuteNonQueryAsync();

        // Pre-build reusable insert command
        _insertCmd = _connection.CreateCommand();
        _insertCmd.Transaction = _transaction;
        _insertCmd.CommandText = string.Format(_schema.InsertRow, _tableName);
        for (int i = 0; i < columnNames.Length; i++)
        {
            var p = _insertCmd.CreateParameter();
            p.ParameterName = $"@p{i}";

            // Npgsql cannot infer types for null/DBNull-only parameters.
            // Set DbType from source column metadata when available.
            if (_backend.DbType == DbPool.DbType.PostgreSql && _columnTypes != null && i < _columnTypes.Length)
                p.DbType = MapClrTypeToDbType(_columnTypes[i].DataType);

            _insertCmd.Parameters.Add(p);
        }

        // Npgsql requires parameter type metadata (or values) before Prepare().
        // We set values at WriteRow time, so skip Prepare for PostgreSQL.
        if (_backend.DbType != DbPool.DbType.PostgreSql)
            _insertCmd.Prepare();
    }

    private static DbType MapClrTypeToDbType(Type? clrType)
    {
        if (clrType == typeof(long) || clrType == typeof(ulong)) return DbType.Int64;
        if (clrType == typeof(int) || clrType == typeof(uint)) return DbType.Int32;
        if (clrType == typeof(short) || clrType == typeof(ushort)) return DbType.Int16;
        if (clrType == typeof(byte) || clrType == typeof(sbyte)) return DbType.Byte;
        if (clrType == typeof(bool)) return DbType.Boolean;
        if (clrType == typeof(float)) return DbType.Single;
        if (clrType == typeof(double)) return DbType.Double;
        if (clrType == typeof(decimal)) return DbType.Decimal;
        if (clrType == typeof(DateTime)) return DbType.DateTime;
        if (clrType == typeof(DateTimeOffset)) return DbType.DateTimeOffset;
        if (clrType == typeof(DateOnly)) return DbType.Date;
        if (clrType == typeof(TimeOnly) || clrType == typeof(TimeSpan)) return DbType.Time;
        if (clrType == typeof(Guid)) return DbType.Guid;
        if (clrType == typeof(byte[])) return DbType.Binary;
        return DbType.String;
    }

    public async Task WriteRow(object[] values)
    {
        if (_insertCmd == null)
            throw new InvalidOperationException("WriteCols must be called before WriteRow");

        for (int i = 0; i < values.Length; i++)
        {
            var value = values[i];

            // Npgsql timestamp with time zone requires UTC DateTime values.
            if (_backend.DbType == DbPool.DbType.PostgreSql)
            {
                if (value is DateTime dt)
                {
                    value = dt.Kind switch
                    {
                        DateTimeKind.Utc => dt,
                        DateTimeKind.Local => dt.ToUniversalTime(),
                        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                    };
                }
                else if (value is DateTimeOffset dto)
                {
                    value = dto.UtcDateTime;
                }
            }

            ((DbParameter)_insertCmd.Parameters[i]).Value = value ?? DBNull.Value;
        }

        await _insertCmd.ExecuteNonQueryAsync();
    }

    public async Task Flush()
    {
        await _transaction.CommitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_insertCmd != null) await _insertCmd.DisposeAsync();
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
