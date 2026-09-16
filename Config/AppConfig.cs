namespace Scarab.Config;

public class AppConfig
{
    public string LogLevel { get; set; } = "Information";
    public int DefaultJobTtlSeconds { get; set; } = 60;
    public string Server { get; set; } = "0.0.0.0:6060";
    public string[] SqlDirectories { get; set; } = ["./Sql"];
    public string Queue { get; set; } = "default";
    public string WorkerName { get; set; } = "default";
    public int WorkerConcurrency { get; set; } = 10;
    public bool WorkerOnly { get; set; }
}
