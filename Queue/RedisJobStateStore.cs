using Scarab.Config;
using StackExchange.Redis;

namespace Scarab.Queue;

public class RedisJobStateStore : IJobStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly StateConfig _config;
    private readonly ILogger<RedisJobStateStore> _logger;

    public RedisJobStateStore(StateConfig config, ILogger<RedisJobStateStore> logger)
    {
        _config = config;
        _logger = logger;
        var options = new ConfigurationOptions
        {
            Password = string.IsNullOrEmpty(config.Password) ? null : config.Password,
            DefaultDatabase = config.Db,
            ConnectTimeout = config.DialTimeoutSeconds * 1000,
            SyncTimeout = config.ReadTimeoutSeconds * 1000,
            AsyncTimeout = config.WriteTimeoutSeconds * 1000,
            AbortOnConnectFail = false,
            Ssl = config.Ssl
        };
        if (config.SslAllowSelfSigned)
            options.CertificateValidation += (_, _, _, _) => true;

        var uniqueAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var addr in config.Addresses)
        {
            if (string.IsNullOrWhiteSpace(addr))
                continue;

            var normalized = addr.Trim();
            if (uniqueAddresses.Add(normalized))
            {
                options.EndPoints.Add(normalized);
            }
            else
            {
                _logger.LogWarning("Skipping duplicate Redis endpoint '{Endpoint}' in state configuration", normalized);
            }
        }

        _redis = ConnectionMultiplexer.Connect(options);
    }

    public async Task SetJobState(string jobId, string status, string? taskName = null, string? queue = null, string? error = null, string? groupId = null)
    {
        var db = _redis.GetDatabase();
        var key = $"job:{jobId}";
        var entries = new List<HashEntry> { new("status", status) };
        if (taskName != null) entries.Add(new("task", taskName));
        if (queue != null) entries.Add(new("queue", queue));
        if (error != null) entries.Add(new("error", error));
        if (groupId != null) entries.Add(new("group_id", groupId));

        await db.HashSetAsync(key, entries.ToArray());
        await db.KeyExpireAsync(key, TimeSpan.FromSeconds(_config.ExpirySeconds));
    }

    public async Task<JobState?> GetJobState(string jobId)
    {
        var db = _redis.GetDatabase();
        var hash = await db.HashGetAllAsync($"job:{jobId}");
        if (hash.Length == 0) return null;

        var state = new JobState { JobId = jobId };
        foreach (var entry in hash)
        {
            switch (entry.Name.ToString())
            {
                case "status": state.Status = entry.Value!; break;
                case "task": state.TaskName = entry.Value; break;
                case "queue": state.Queue = entry.Value; break;
                case "error": state.Error = entry.Value; break;
                case "result": _ = int.TryParse(entry.Value.ToString(), out var r); state.Result = r; break;
                case "group_id": state.GroupId = entry.Value; break;
            }
        }
        return state;
    }

    public async Task SetJobResult(string jobId, int rowCount)
    {
        var db = _redis.GetDatabase();
        await db.HashSetAsync($"job:{jobId}", "result", rowCount.ToString());
    }

    public async Task DeleteJob(string jobId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"job:{jobId}");
    }

    public async Task CreateGroup(string groupId, string[] jobIds)
    {
        var db = _redis.GetDatabase();
        var key = $"group:{groupId}";
        var jobsKey = $"group:{groupId}:jobs";

        await db.HashSetAsync(key, "status", "started");
        foreach (var jid in jobIds)
            await db.SetAddAsync(jobsKey, jid);

        var expiry = TimeSpan.FromSeconds(_config.MetaExpirySeconds);
        await db.KeyExpireAsync(key, expiry);
        await db.KeyExpireAsync(jobsKey, expiry);
    }

    public async Task<GroupState?> GetGroupState(string groupId)
    {
        var db = _redis.GetDatabase();
        var key = $"group:{groupId}";
        var jobsKey = $"group:{groupId}:jobs";

        var status = await db.HashGetAsync(key, "status");
        if (!status.HasValue) return null;

        var jobIdValues = await db.SetMembersAsync(jobsKey);
        return new GroupState
        {
            GroupId = groupId,
            Status = status!,
            JobIds = jobIdValues.Select(v => v.ToString()).ToArray()
        };
    }

    public async Task DeleteGroup(string groupId)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"group:{groupId}");
        await db.KeyDeleteAsync($"group:{groupId}:jobs");
    }
}
