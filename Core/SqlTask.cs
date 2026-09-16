using Scarab.ResultBackends;

namespace Scarab.Core;

public class SqlTask
{
    public string Name { get; set; } = "";
    public string Queue { get; set; } = "default";
    public int Concurrency { get; set; }
    public string RawSql { get; set; } = "";
    public bool IsPrepared { get; set; } = true;
    public DbPool.DbPool SourceDbs { get; set; } = new();
    public ResultBackendCollection ResultBackends { get; set; } = new();
}
