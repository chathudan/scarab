using Scarab.ResultBackends;

namespace Scarab.Core;

public class SqlTask
{
    public string Name { get; set; } = "";
    public string Queue { get; set; } = "default";
    public int Concurrency { get; set; }
    public string RawSql { get; set; } = "";
    public bool IsPrepared { get; set; } = true;

    /// <summary>
    /// Names for this task's positional `?`/`@p1`/`$1` args, in order, from the
    /// `-- params:` tag. Purely documentation - not validated against the query or
    /// the job request - so callers (and the /tasks endpoint) can tell what a task's
    /// args mean without reading its SQL.
    /// </summary>
    public string[] Params { get; set; } = [];

    public DbPool.DbPool SourceDbs { get; set; } = new();
    public ResultBackendCollection ResultBackends { get; set; } = new();
}
