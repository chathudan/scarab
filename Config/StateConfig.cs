namespace Scarab.Config;

public class StateConfig : BrokerConfig
{
    public int ExpirySeconds { get; set; } = 3000;
    public int MetaExpirySeconds { get; set; } = 3600;
}
