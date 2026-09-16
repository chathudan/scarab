namespace Scarab.Config;

public class ResultDbConfig : DbPoolConfig
{
    public string ResultsTable { get; set; } = "results_{0}";
    public bool Unlogged { get; set; }
}
