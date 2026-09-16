using Scarab.Config;

namespace Scarab.DbPool;

public class DbPoolManager
{
    private readonly ILogger<DbPoolManager> _logger;

    public DbPoolManager(ILogger<DbPoolManager> logger)
    {
        _logger = logger;
    }

    public async Task<DbPool> InitializeAsync(Dictionary<string, DbPoolConfig> configs)
    {
        var pool = new DbPool();
        foreach (var (name, config) in configs)
        {
            _logger.LogInformation("Initializing source DB: {Name} ({Type})", name, config.Type);
            var factory = new DbConnectionFactory(name, config);

            try
            {
                await factory.PingAsync();
            }
            catch (Exception ex) when (config.Optional)
            {
                _logger.LogWarning(ex, "Optional source DB '{Name}' is unreachable; skipping it. Tasks referencing it will fail until it becomes available.", name);
                continue;
            }

            pool[name] = factory;
            _logger.LogInformation("Source DB '{Name}' connected", name);
        }
        return pool;
    }

    public async Task<DbPool> InitializeResultsAsync(Dictionary<string, ResultDbConfig> configs)
    {
        var pool = new DbPool();
        foreach (var (name, config) in configs)
        {
            var dbType = DbConnectionFactory.ParseDbType(config.Type);
            if (dbType == DbType.Mssql)
                throw new InvalidOperationException($"Result backend '{name}' cannot use MSSQL. Only MySQL and PostgreSQL are supported.");

            _logger.LogInformation("Initializing result DB: {Name} ({Type})", name, config.Type);
            var factory = new DbConnectionFactory(name, config);
            await factory.PingAsync();
            pool[name] = factory;
            _logger.LogInformation("Result DB '{Name}' connected", name);
        }
        return pool;
    }
}
