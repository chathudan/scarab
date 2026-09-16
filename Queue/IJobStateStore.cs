namespace Scarab.Queue;

public interface IJobStateStore
{
    Task SetJobState(string jobId, string status, string? taskName = null, string? queue = null, string? error = null, string? groupId = null);
    Task<JobState?> GetJobState(string jobId);
    Task SetJobResult(string jobId, int rowCount);
    Task DeleteJob(string jobId);
    Task CreateGroup(string groupId, string[] jobIds);
    Task<GroupState?> GetGroupState(string groupId);
    Task DeleteGroup(string groupId);
}
