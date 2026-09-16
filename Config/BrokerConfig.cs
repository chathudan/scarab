namespace Scarab.Config;

public class BrokerConfig
{
    public string Type { get; set; } = "redis";
    public string[] Addresses { get; set; } = ["localhost:6379"];
    public string Password { get; set; } = "";
    public bool Ssl { get; set; }

    /// <summary>
    /// Skips certificate validation for the TLS connection. Only meant for the
    /// self-signed dev certificate Aspire's local Redis resource issues - never set
    /// this for a real deployment's Redis.
    /// </summary>
    public bool SslAllowSelfSigned { get; set; }

    public int Db { get; set; }
    public int MaxActive { get; set; } = 50;
    public int MaxIdle { get; set; } = 20;
    public int DialTimeoutSeconds { get; set; } = 1;
    public int ReadTimeoutSeconds { get; set; } = 1;
    public int WriteTimeoutSeconds { get; set; } = 1;
}
