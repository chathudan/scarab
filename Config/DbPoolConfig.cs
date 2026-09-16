namespace Scarab.Config;

public class DbPoolConfig
{
    public string Type { get; set; } = "";
    public string Dsn { get; set; } = "";
    public int MaxIdle { get; set; } = 10;
    public int MaxActive { get; set; } = 100;
    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// When true, a failed connectivity check at startup logs a warning and excludes this DB
    /// from the pool instead of crashing the app. Intended for demo/sample source databases
    /// that shouldn't be a hard dependency for production startup.
    /// </summary>
    public bool Optional { get; set; }
}
