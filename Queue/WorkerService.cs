using Scarab.Config;
using Scarab.Core;

namespace Scarab.Queue;

public class WorkerService : BackgroundService
{
    private readonly ScarabCore _core;
    private readonly IJobBroker _broker;
    private readonly IJobStateStore _stateStore;
    private readonly AppConfig _appConfig;
    private readonly ILogger<WorkerService> _logger;

    public WorkerService(ScarabCore core, IJobBroker broker, IJobStateStore stateStore, AppConfig appConfig, ILogger<WorkerService> logger)
    {
        _core = core;
        _broker = broker;
        _stateStore = stateStore;
        _appConfig = appConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WorkerService starting: name={Worker}, concurrency={Concurrency}, queue={Queue}", _appConfig.WorkerName, _appConfig.WorkerConcurrency, _appConfig.Queue);

        var tasks = new List<Task>();

        // Delayed job promoter
        tasks.Add(DelayedJobPromoter(_appConfig.Queue, stoppingToken));

        // Worker loops
        for (int i = 0; i < _appConfig.WorkerConcurrency; i++)
        {
            var workerId = i;
            tasks.Add(WorkerLoop(workerId, _appConfig.Queue, stoppingToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task WorkerLoop(int workerId, string queue, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker {WorkerId} started on queue:{Queue}", workerId, queue);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = await _broker.Dequeue(queue, stoppingToken);
                if (message == null) continue;

                await ProcessJob(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker {WorkerId} encountered an error", workerId);
            }
        }

        _logger.LogInformation("Worker {WorkerId} stopped", workerId);
    }

    private async Task ProcessJob(JobMessage message, CancellationToken stoppingToken)
    {
        var tasks = _core.GetTasks();
        if (!tasks.TryGetValue(message.TaskName, out var task))
        {
            _logger.LogError("Unknown task '{Task}' for job {JobId}", message.TaskName, message.JobId);
            await _stateStore.SetJobState(message.JobId, "failed", error: $"Unknown task: {message.TaskName}");
            return;
        }

        var ttl = TimeSpan.FromSeconds(message.TtlSeconds > 0 ? message.TtlSeconds : _appConfig.DefaultJobTtlSeconds);
        var args = message.Args ?? [];

        try
        {
            await _core.ExecJob(message.JobId, message.TaskName, message.Db, ttl, args, task, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw; // Propagate host shutdown
        }
        catch (Exception ex)
        {
            // Retry logic
            if (message.Retries < message.MaxRetries)
            {
                message.Retries++;
                _logger.LogWarning(ex, "Job {JobId} failed, retrying ({Retries}/{MaxRetries})", message.JobId, message.Retries, message.MaxRetries);
                await _stateStore.SetJobState(message.JobId, "retrying", error: ex.Message);
                await _broker.Enqueue(message.Queue, message);
            }
            else
            {
                _logger.LogError(ex, "Job {JobId} failed permanently after {MaxRetries} retries", message.JobId, message.MaxRetries);
            }
        }
    }

    private async Task DelayedJobPromoter(string queue, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _broker.PromoteDelayedJobs(queue);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error promoting delayed jobs");
            }
        }
    }
}
