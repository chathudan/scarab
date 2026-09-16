namespace Scarab.ResultBackends;

public interface IResultBackend
{
    Task<IResultSet> NewResultSet(string jobId, string taskName, TimeSpan ttl);
}
