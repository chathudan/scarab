namespace Scarab.Queue;

public interface IJobBroker
{
    Task Enqueue(string queue, JobMessage message);
    Task<JobMessage?> Dequeue(string queue, CancellationToken cancellationToken);
    Task<List<JobMessage>> GetPending(string queue);
    Task EnqueueDelayed(string queue, JobMessage message, DateTime eta);
    Task PromoteDelayedJobs(string queue);
    Task<bool> RemoveJob(string queue, string jobId);
}
