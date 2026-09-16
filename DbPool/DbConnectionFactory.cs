using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Scarab.Config;

namespace Scarab.DbPool;

public class DbConnectionFactory
{
    public string Name { get; }
    public DbType Type { get; }
    private readonly string _connectionString;

    public DbConnectionFactory(string name, DbPoolConfig config)
    {
        Name = name;
        Type = ParseDbType(config.Type);
        _connectionString = BuildConnectionString(config);
    }

    public async Task<DbConnection> CreateConnectionAsync()
    {
        DbConnection conn = Type switch
        {
            DbType.Mssql => new SqlConnection(_connectionString),
            DbType.MySql => new MySqlConnection(_connectionString),
            DbType.PostgreSql => new NpgsqlConnection(_connectionString),
            _ => throw new InvalidOperationException($"Unsupported DB type: {Type}")
        };
        await conn.OpenAsync();
        return conn;
    }

    public async Task PingAsync()
    {
        await using var conn = await CreateConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Type == DbType.Mssql ? "SELECT 1" : "SELECT 1";
        await cmd.ExecuteScalarAsync();
    }

    private string BuildConnectionString(DbPoolConfig config)
    {
        var dsn = config.Dsn;
        // Inject pool sizes into the connection string if not already present
        return Type switch
        {
            DbType.Mssql => AppendIfMissing(dsn, "Min Pool Size", config.MaxIdle.ToString(), "Max Pool Size", config.MaxActive.ToString(), "Connect Timeout", config.ConnectTimeoutSeconds.ToString()),
            DbType.MySql => AppendIfMissing(dsn, "MinimumPoolSize", config.MaxIdle.ToString(), "MaximumPoolSize", config.MaxActive.ToString(), "ConnectionTimeout", config.ConnectTimeoutSeconds.ToString()),
            DbType.PostgreSql => AppendIfMissing(dsn, "Minimum Pool Size", config.MaxIdle.ToString(), "Maximum Pool Size", config.MaxActive.ToString(), "Timeout", config.ConnectTimeoutSeconds.ToString()),
            _ => dsn
        };
    }

    private static string AppendIfMissing(string dsn, params string[] kvPairs)
    {
        var result = dsn.TrimEnd(';');
        for (int i = 0; i < kvPairs.Length; i += 2)
        {
            if (!dsn.Contains(kvPairs[i], StringComparison.OrdinalIgnoreCase))
                result += $";{kvPairs[i]}={kvPairs[i + 1]}";
        }
        return result + ";";
    }

    public static DbType ParseDbType(string type) => type.ToLowerInvariant() switch
    {
        "mssql" => DbType.Mssql,
        "mysql" => DbType.MySql,
        "postgres" or "postgresql" => DbType.PostgreSql,
        _ => throw new ArgumentException($"Unknown database type: {type}")
    };
}
