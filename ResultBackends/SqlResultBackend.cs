using System.Collections.Concurrent;
using System.Data.Common;
using Scarab.Config;
using Scarab.DbPool;

namespace Scarab.ResultBackends;

public class SqlResultBackend : IResultBackend
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ResultDbConfig _config;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, InsertSchema> _schemaCache = new();

    public SqlResultBackend(DbConnectionFactory connectionFactory, ResultDbConfig config, ILogger logger)
    {
        _connectionFactory = connectionFactory;
        _config = config;
        _logger = logger;
    }

    public DbPool.DbType DbType => _connectionFactory.Type;
    public ResultDbConfig Config => _config;
    internal ConcurrentDictionary<string, InsertSchema> SchemaCache => _schemaCache;

    public async Task<IResultSet> NewResultSet(string jobId, string taskName, TimeSpan ttl)
    {
        var tableName = string.Format(_config.ResultsTable, jobId.Replace("-", "_"));
        var conn = await _connectionFactory.CreateConnectionAsync();
        var txn = await conn.BeginTransactionAsync();
        return new SqlResultSet(jobId, taskName, tableName, this, conn, txn);
    }

    public async Task<long> CountJobData(string jobId)
    {
        var tableName = string.Format(_config.ResultsTable, jobId.Replace("-", "_"));
        await using var conn = await _connectionFactory.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();

        cmd.CommandText = _connectionFactory.Type switch
        {
            DbType.PostgreSql => $"SELECT COUNT(*) FROM \"{tableName}\"",
            _ => $"SELECT COUNT(*) FROM `{tableName}`"
        };

        var result = await cmd.ExecuteScalarAsync();
        if (result == null || result is DBNull)
            return 0;

        return Convert.ToInt64(result);
    }

    public async Task<List<Dictionary<string, object?>>> ReadJobData(string jobId, int? limit = null, int offset = 0)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "offset must be >= 0");

        var tableName = string.Format(_config.ResultsTable, jobId.Replace("-", "_"));
        await using var conn = await _connectionFactory.CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();

        // Pagination values are validated by caller and inserted as numeric literals.
        string pagingClause;
        if (limit.HasValue)
            pagingClause = $" LIMIT {limit.Value} OFFSET {offset}";
        else if (offset > 0)
            pagingClause = _connectionFactory.Type == DbType.PostgreSql ? $" OFFSET {offset}" : $" LIMIT 18446744073709551615 OFFSET {offset}";
        else
            pagingClause = string.Empty;

        cmd.CommandText = _connectionFactory.Type switch
        {
            DbType.PostgreSql => $"SELECT * FROM \"{tableName}\"{pagingClause}",
            _ => $"SELECT * FROM `{tableName}`{pagingClause}"
        };

        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[reader.GetName(i)] = value is DBNull ? null : value;
            }
            rows.Add(row);
        }

        return rows;
    }

    internal InsertSchema BuildSchema(string taskName, string[] columnNames, DbColumn[] columnTypes)
    {
        var dbType = _connectionFactory.Type;
        var sanitizedNames = SanitizeIdentifiers(columnNames);
        var cols = new List<string>();

        for (int i = 0; i < sanitizedNames.Length; i++)
        {
            var sqlType = MapClrTypeToSql(columnTypes[i], dbType);
            cols.Add($"{sanitizedNames[i]} {sqlType}");
        }

        var colDefs = string.Join(", ", cols);
        var colList = string.Join(", ", sanitizedNames);

        string dropTpl, createTpl, insertTpl;

        if (dbType == DbPool.DbType.PostgreSql)
        {
            dropTpl = "DROP TABLE IF EXISTS {0}";
            var unlogged = _config.Unlogged ? "UNLOGGED " : "";
            createTpl = $"CREATE {unlogged}TABLE {{0}} ({colDefs})";
            var placeholders = string.Join(", ", Enumerable.Range(0, columnNames.Length).Select(i => $"@p{i}"));
            insertTpl = $"INSERT INTO {{0}} ({colList}) VALUES ({placeholders})";
        }
        else // MySQL
        {
            dropTpl = "DROP TABLE IF EXISTS {0}";
            createTpl = $"CREATE TABLE {{0}} ({colDefs})";
            var placeholders = string.Join(", ", Enumerable.Repeat("?", columnNames.Length));
            insertTpl = $"INSERT INTO {{0}} ({colList}) VALUES ({placeholders})";
        }

        var schema = new InsertSchema { DropTable = dropTpl, CreateTable = createTpl, InsertRow = insertTpl };
        _schemaCache[taskName] = schema;
        return schema;
    }

    private static string SanitizeIdentifier(string name)
    {
        // Allow only alphanumeric and underscores
        var sanitized = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return string.IsNullOrEmpty(sanitized) ? "col" : sanitized;
    }

    private static string[] SanitizeIdentifiers(string[] names)
    {
        // Two source columns can sanitize to the same identifier (e.g. "user-id" and "user id"
        // both become "userid"); disambiguate collisions so CREATE TABLE doesn't fail on a
        // duplicate column name.
        var seenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new string[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            var baseName = SanitizeIdentifier(names[i]);
            if (!seenCounts.TryGetValue(baseName, out var count))
            {
                seenCounts[baseName] = 0;
                result[i] = baseName;
            }
            else
            {
                count++;
                seenCounts[baseName] = count;
                result[i] = $"{baseName}_{count}";
            }
        }

        return result;
    }

    private static string MapClrTypeToSql(DbColumn col, DbPool.DbType dbType)
    {
        var clrType = col.DataType;
        if (clrType == null) return dbType == DbPool.DbType.PostgreSql ? "TEXT" : "TEXT";

        if (clrType == typeof(long) || clrType == typeof(ulong)) return "BIGINT";
        if (clrType == typeof(int) || clrType == typeof(uint)) return dbType == DbPool.DbType.PostgreSql ? "INTEGER" : "INT";
        if (clrType == typeof(short) || clrType == typeof(ushort)) return "SMALLINT";
        if (clrType == typeof(byte) || clrType == typeof(sbyte)) return "SMALLINT";
        if (clrType == typeof(bool)) return "BOOLEAN";
        if (clrType == typeof(float)) return dbType == DbPool.DbType.PostgreSql ? "REAL" : "FLOAT";
        if (clrType == typeof(double)) return "DOUBLE PRECISION";
        if (clrType == typeof(decimal))
        {
            var precision = col.NumericPrecision ?? 18;
            var scale = col.NumericScale ?? 4;
            return $"NUMERIC({precision},{scale})";
        }
        if (clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset)) return dbType == DbPool.DbType.PostgreSql ? "TIMESTAMP" : "DATETIME";
        if (clrType == typeof(DateOnly)) return "DATE";
        if (clrType == typeof(TimeOnly) || clrType == typeof(TimeSpan)) return "TIME";
        if (clrType == typeof(Guid)) return dbType == DbPool.DbType.PostgreSql ? "UUID" : "CHAR(36)";
        if (clrType == typeof(byte[])) return dbType == DbPool.DbType.PostgreSql ? "BYTEA" : "BLOB";

        // String and fallback
        var size = col.ColumnSize;
        if (size.HasValue && size.Value > 0 && size.Value <= 8000)
            return dbType == DbPool.DbType.PostgreSql ? $"VARCHAR({size.Value})" : $"VARCHAR({size.Value})";

        return "TEXT";
    }
}
