using System.Text.Json;
using Scarab.Config;
using StackExchange.Redis;

namespace Scarab.Queue;

public class RedisJobBroker : IJobBroker
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisJobBroker> _logger;

    public RedisJobBroker(BrokerConfig config, ILogger<RedisJobBroker> logger)
    {
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
                _logger.LogWarning("Skipping duplicate Redis endpoint '{Endpoint}' in broker configuration", normalized);
            }
        }

        _redis = ConnectionMultiplexer.Connect(options);
    }

    public async Task Enqueue(string queue, JobMessage message)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(message);
        await db.ListLeftPushAsync($"queue:{queue}", json);
        _logger.LogDebug("Enqueued job {JobId} to queue:{Queue}", message.JobId, queue);
    }

    public async Task<JobMessage?> Dequeue(string queue, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        while (!cancellationToken.IsCancellationRequested)
        {
            var value = await db.ListRightPopAsync($"queue:{queue}");
            if (value.HasValue)
            {
                var msg = JsonSerializer.Deserialize<JobMessage>(value.ToString());
                _logger.LogDebug("Dequeued job {JobId} from queue:{Queue}", msg?.JobId, queue);
                return msg;
            }
            await Task.Delay(500, cancellationToken);
        }
        return null;
    }

    public async Task<List<JobMessage>> GetPending(string queue)
    {
        var db = _redis.GetDatabase();
        var items = await db.ListRangeAsync($"queue:{queue}");
        var result = new List<JobMessage>();
        foreach (var item in items)
        {
            var msg = JsonSerializer.Deserialize<JobMessage>(item.ToString());
            if (msg != null) result.Add(msg);
        }
        return result;
    }

    public async Task EnqueueDelayed(string queue, JobMessage message, DateTime eta)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(message);
        var score = new DateTimeOffset(eta.ToUniversalTime()).ToUnixTimeSeconds();
        await db.SortedSetAddAsync($"queue:{queue}:delayed", json, score);
        _logger.LogDebug("Enqueued delayed job {JobId} to queue:{Queue}:delayed, ETA={Eta}", message.JobId, queue, eta);
    }

    public async Task<bool> RemoveJob(string queue, string jobId)
    {
        var db = _redis.GetDatabase();
        var removed = false;

        var pending = await db.ListRangeAsync($"queue:{queue}");
        foreach (var item in pending)
        {
            var msg = TryDeserialize(item);
            if (msg?.JobId != jobId) continue;

            var count = await db.ListRemoveAsync($"queue:{queue}", item);
            if (count > 0) removed = true;
        }

        var delayedKey = $"queue:{queue}:delayed";
        var delayed = await db.SortedSetRangeByScoreAsync(delayedKey);
        foreach (var item in delayed)
        {
            var msg = TryDeserialize(item);
            if (msg?.JobId != jobId) continue;

            var ok = await db.SortedSetRemoveAsync(delayedKey, item);
            if (ok) removed = true;
        }

        if (removed)
            _logger.LogDebug("Removed job {JobId} from queue:{Queue} (and delayed set)", jobId, queue);

        return removed;
    }

    private JobMessage? TryDeserialize(RedisValue item)
    {
        try { return JsonSerializer.Deserialize<JobMessage>(item.ToString()); }
        catch (JsonException) { return null; }
    }

    public async Task PromoteDelayedJobs(string queue)
    {
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dueJobs = await db.SortedSetRangeByScoreAsync($"queue:{queue}:delayed", double.NegativeInfinity, now);
        foreach (var item in dueJobs)
        {
            await db.SortedSetRemoveAsync($"queue:{queue}:delayed", item);
            await db.ListLeftPushAsync($"queue:{queue}", item);
            _logger.LogDebug("Promoted delayed job to queue:{Queue}", queue);
        }
    }
}
