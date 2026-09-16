using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using MySqlConnector;
using Npgsql;
using Scarab.Config;
using Scarab.Models;
using Scarab.Queue;
using Scarab.ResultBackends;

namespace Scarab.Core;

public partial class ScarabCore
{
    private readonly AppConfig _appConfig;
    private readonly DbPool.DbPool _srcDbs;
    private readonly ResultBackendCollection _resultBackends;
    private readonly IJobBroker _broker;
    private readonly IJobStateStore _stateStore;
    private readonly ILogger<ScarabCore> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobCts = new();

    public TaskCollection Tasks { get; set; } = new();

    public ScarabCore(AppConfig appConfig, DbPool.DbPool srcDbs, ResultBackendCollection resultBackends, IJobBroker broker, IJobStateStore stateStore, ILogger<ScarabCore> logger)
    {
        _appConfig = appConfig;
        _srcDbs = srcDbs;
        _resultBackends = resultBackends;
        _broker = broker;
        _stateStore = stateStore;
        _logger = logger;
    }

    public TaskCollection GetTasks() => Tasks;

    public async Task<JobResp> NewJob(JobReq request, string taskName)
    {
        if (!Tasks.TryGetValue(taskName, out var task))
            throw new InvalidOperationException($"Unknown task: {taskName}");

        var msg = MakeJob(request, taskName, task);

        if (msg.Eta.HasValue && msg.Eta.Value > DateTime.UtcNow)
            await _broker.EnqueueDelayed(msg.Queue, msg, msg.Eta.Value);
        else
            await _broker.Enqueue(msg.Queue, msg);

        await _stateStore.SetJobState(msg.JobId, "pending", taskName, msg.Queue, groupId: msg.GroupId);

        return new JobResp
        {
            JobId = msg.JobId,
            TaskName = taskName,
            Queue = msg.Queue,
            Eta = msg.Eta,
            Retries = msg.MaxRetries
        };
    }

    public async Task<GroupResp> NewJobGroup(GroupReq request)
    {
        var groupId = string.IsNullOrWhiteSpace(request.GroupId) ? Guid.NewGuid().ToString("N") : request.GroupId;
        var jobResps = new List<JobResp>();
        var jobIds = new List<string>();

        foreach (var jobReq in request.Jobs)
        {
            if (string.IsNullOrWhiteSpace(jobReq.TaskName))
                throw new InvalidOperationException("Each job in a group must specify a 'task' name");

            var resp = await NewJob(jobReq, jobReq.TaskName);
            jobResps.Add(resp);
            jobIds.Add(resp.JobId);

            // Update job state with group reference
            await _stateStore.SetJobState(resp.JobId, "pending", groupId: groupId);
        }

        await _stateStore.CreateGroup(groupId, jobIds.ToArray());
        return new GroupResp { GroupId = groupId, Jobs = jobResps };
    }

    public async Task<JobStatusResp> GetJobStatus(string jobId)
    {
        var state = await _stateStore.GetJobState(jobId);
        if (state == null)
            throw new KeyNotFoundException($"Job '{jobId}' not found");

        return new JobStatusResp
        {
            JobId = jobId,
            State = MapState(state.Status),
            Count = state.Result,
            Error = state.Error ?? ""
        };
    }

    public async Task<List<Dictionary<string, object?>>> GetJobData(string jobId, int limit)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "limit must be greater than 0");
        if (limit > 5000) throw new ArgumentOutOfRangeException(nameof(limit), "limit must be <= 5000");

        await EnsureJobCompleted(jobId);

        Exception? lastError = null;
        foreach (var backend in _resultBackends.Values)
        {
            if (backend is not SqlResultBackend sqlBackend)
                continue;

            try
            {
                return await sqlBackend.ReadJobData(jobId, limit, 0);
            }
            catch (Exception ex) when (IsMissingTableError(ex))
            {
                // Table doesn't exist on this backend; continue searching across configured result backends.
                lastError = ex;
            }
        }

        throw new KeyNotFoundException(lastError == null
            ? $"Result table for job '{jobId}' not found"
            : $"Result table for job '{jobId}' not found. Last error: {lastError.Message}");
    }

    public async Task<List<Dictionary<string, object?>>> GetAllJobData(string jobId)
    {
        await EnsureJobCompleted(jobId);

        Exception? lastError = null;
        foreach (var backend in _resultBackends.Values)
        {
            if (backend is not SqlResultBackend sqlBackend)
                continue;

            try
            {
                return await sqlBackend.ReadJobData(jobId);
            }
            catch (Exception ex) when (IsMissingTableError(ex))
            {
                // Table doesn't exist on this backend; continue searching across configured result backends.
                lastError = ex;
            }
        }

        throw new KeyNotFoundException(lastError == null
            ? $"Result table for job '{jobId}' not found"
            : $"Result table for job '{jobId}' not found. Last error: {lastError.Message}");
    }

    public async Task<(long Total, List<Dictionary<string, object?>> Rows)> GetJobDataPage(string jobId, int page, int pageSize)
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page), "page must be greater than 0");
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize), "page_size must be greater than 0");
        if (pageSize > 5000) throw new ArgumentOutOfRangeException(nameof(pageSize), "page_size must be <= 5000");

        await EnsureJobCompleted(jobId);

        var offset = (page - 1) * pageSize;
        Exception? lastError = null;
        foreach (var backend in _resultBackends.Values)
        {
            if (backend is not SqlResultBackend sqlBackend)
                continue;

            try
            {
                var total = await sqlBackend.CountJobData(jobId);
                var rows = await sqlBackend.ReadJobData(jobId, pageSize, offset);
                return (total, rows);
            }
            catch (Exception ex) when (IsMissingTableError(ex))
            {
                // Table doesn't exist on this backend; continue searching across configured result backends.
                lastError = ex;
            }
        }

        throw new KeyNotFoundException(lastError == null
            ? $"Result table for job '{jobId}' not found"
            : $"Result table for job '{jobId}' not found. Last error: {lastError.Message}");
    }

    private async Task EnsureJobCompleted(string jobId)
    {
        var state = await _stateStore.GetJobState(jobId);
        if (state == null)
            throw new KeyNotFoundException($"Job '{jobId}' not found");
        if (state.Status != "done")
            throw new InvalidOperationException($"Job '{jobId}' is not completed. Current state: {MapState(state.Status)}");
    }

    public async Task<GroupStatusResp> GetJobGroupStatus(string groupId)
    {
        var group = await _stateStore.GetGroupState(groupId);
        if (group == null)
            throw new KeyNotFoundException($"Group '{groupId}' not found");

        var jobs = new List<JobStatusResp>();
        var allDone = true;
        var anyFailed = false;

        foreach (var jobId in group.JobIds)
        {
            var state = await _stateStore.GetJobState(jobId);
            if (state == null) continue;

            jobs.Add(new JobStatusResp
            {
                JobId = jobId,
                State = MapState(state.Status),
                Count = state.Result,
                Error = state.Error ?? ""
            });

            if (state.Status is not "done" and not "failed") allDone = false;
            if (state.Status == "failed") anyFailed = true;
        }

        var groupState = allDone ? (anyFailed ? "FAILURE" : "SUCCESS") : "STARTED";
        return new GroupStatusResp { GroupId = groupId, State = groupState, Jobs = jobs };
    }

    public async Task<List<JobMessage>> GetPendingJobs(string queue)
    {
        return await _broker.GetPending(queue);
    }

    public async Task CancelJob(string jobId, bool purge)
    {
        var state = await _stateStore.GetJobState(jobId);
        if (state == null)
            throw new KeyNotFoundException($"Job '{jobId}' not found");

        if (!purge && state.Status is "done" or "failed")
            throw new InvalidOperationException($"Cannot cancel completed job '{jobId}'. Use purge=true to force delete.");

        if (_jobCts.TryRemove(jobId, out var cts))
            await cts.CancelAsync();
        else if (!string.IsNullOrWhiteSpace(state.Queue))
            await _broker.RemoveJob(state.Queue, jobId);

        await _stateStore.DeleteJob(jobId);
    }

    public async Task CancelJobGroup(string groupId, bool purge)
    {
        var group = await _stateStore.GetGroupState(groupId);
        if (group == null)
            throw new KeyNotFoundException($"Group '{groupId}' not found");

        foreach (var jobId in group.JobIds)
        {
            try { await CancelJob(jobId, purge); }
            catch (KeyNotFoundException) { /* already cleaned up */ }
            catch (InvalidOperationException) when (!purge) { /* skip completed */ }
        }

        await _stateStore.DeleteGroup(groupId);
    }

    public async Task<long> ExecJob(string jobId, string taskName, string? dbName, TimeSpan ttl, object[] args, SqlTask task, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _jobCts[jobId] = cts;
        var token = cts.Token;

        try
        {
            await _stateStore.SetJobState(jobId, "started");

            // Pick source DB
            string srcName;
            DbPool.DbConnectionFactory srcFactory;
            if (!string.IsNullOrWhiteSpace(dbName))
            {
                srcFactory = task.SourceDbs.Get(dbName);
                srcName = dbName;
            }
            else
            {
                (srcName, srcFactory) = task.SourceDbs.GetRandom();
            }

            _logger.LogInformation("Executing job {JobId} task={Task} db={Db}", jobId, taskName, srcName);

            await using var conn = await srcFactory.CreateConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = task.RawSql;

            // Bind parameters
            if (args.Length > 0)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = srcFactory.Type switch
                    {
                        DbPool.DbType.Mssql => $"@p{i + 1}",
                        DbPool.DbType.PostgreSql => $"${i + 1}",
                        _ => $"@p{i}" // MySQL uses positional ? but we add named params for clarity
                    };
                    p.Value = ConvertArg(args[i]);
                    cmd.Parameters.Add(p);
                }
            }

            if (task.IsPrepared) await cmd.PrepareAsync(token);

            await using var reader = await cmd.ExecuteReaderAsync(token);
            var rowCount = await WriteResults(jobId, task, ttl, reader, token);

            await _stateStore.SetJobState(jobId, "done");
            await _stateStore.SetJobResult(jobId, (int)rowCount);
            _logger.LogInformation("Job {JobId} completed: {RowCount} rows", jobId, rowCount);

            return rowCount;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Job {JobId} was cancelled", jobId);
            await _stateStore.SetJobState(jobId, "failed", error: "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", jobId);
            await _stateStore.SetJobState(jobId, "failed", error: ex.Message);
            throw;
        }
        finally
        {
            _jobCts.TryRemove(jobId, out _);
            cts.Dispose();
        }
    }

    private async Task<long> WriteResults(string jobId, SqlTask task, TimeSpan ttl, DbDataReader reader, CancellationToken cancellationToken)
    {
        if (task.ResultBackends.Count == 0) return 0;

        var (_, backend) = task.ResultBackends.GetRandom();
        await using var resultSet = await backend.NewResultSet(jobId, task.Name, ttl);

        // Read column schema
        var schema = await reader.GetColumnSchemaAsync(cancellationToken);
        var columnNames = schema.Select(c => c.ColumnName).ToArray();
        var columnTypes = schema.ToArray();

        if (!resultSet.IsColTypesRegistered())
            await resultSet.RegisterColTypes(columnNames, columnTypes);

        await resultSet.WriteCols(columnNames);

        long count = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            await resultSet.WriteRow(values);
            count++;
        }

        await resultSet.Flush();
        return count;
    }

    private JobMessage MakeJob(JobReq request, string taskName, SqlTask task)
    {
        var jobId = string.IsNullOrWhiteSpace(request.JobId) ? Guid.NewGuid().ToString("N") : request.JobId;

        if (!string.IsNullOrWhiteSpace(request.JobId) && !JobIdRegex().IsMatch(request.JobId))
            throw new ArgumentException("job_id can only contain a-z, 0-9, -, _, :");

        var queue = request.Queue ?? task.Queue ?? _appConfig.Queue;
        var ttl = request.Ttl > 0 ? request.Ttl : _appConfig.DefaultJobTtlSeconds;

        DateTime? eta = null;
        if (!string.IsNullOrWhiteSpace(request.Eta) && DateTime.TryParse(request.Eta, out var parsed))
            eta = parsed.ToUniversalTime();

        return new JobMessage
        {
            JobId = jobId,
            TaskName = taskName,
            Queue = queue,
            Args = request.Args?.Cast<object>().ToArray(),
            Db = request.Db,
            TtlSeconds = ttl,
            MaxRetries = request.Retries,
            Retries = 0,
            Eta = eta,
            GroupId = null
        };
    }

    private static bool IsMissingTableError(Exception ex) => ex switch
    {
        PostgresException pg => pg.SqlState == PostgresErrorCodes.UndefinedTable,
        MySqlException my => my.Number == 1146, // ER_NO_SUCH_TABLE
        _ => false
    };

    private static string MapState(string internalState) => internalState switch
    {
        "pending" => "PENDING",
        "started" or "processing" => "STARTED",
        "done" => "SUCCESS",
        "failed" => "FAILURE",
        "retrying" => "RETRY",
        _ => internalState.ToUpperInvariant()
    };

    private static object ConvertArg(object? arg)
    {
        if (arg is null || arg == DBNull.Value)
            return DBNull.Value;

        if (arg is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString() ?? "",
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => DBNull.Value,
                _ => je.GetRawText()
            };
        }

        return arg;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9\-_:]+$")]
    private static partial Regex JobIdRegex();
}
