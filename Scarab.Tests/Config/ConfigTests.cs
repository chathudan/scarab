using Scarab.Config;

namespace Scarab.Tests.Config;

public class ConfigTests
{
    [Fact]
    public void AppConfig_Defaults()
    {
        var config = new AppConfig();

        Assert.Equal("Information", config.LogLevel);
        Assert.Equal(60, config.DefaultJobTtlSeconds);
        Assert.Equal("0.0.0.0:6060", config.Server);
        Assert.Equal("default", config.Queue);
        Assert.Equal("default", config.WorkerName);
        Assert.Equal(10, config.WorkerConcurrency);
        Assert.False(config.WorkerOnly);
    }

    [Fact]
    public void BrokerConfig_Defaults()
    {
        var config = new BrokerConfig();

        Assert.Equal("redis", config.Type);
        Assert.Single(config.Addresses);
        Assert.Equal("localhost:6379", config.Addresses[0]);
        Assert.Equal(0, config.Db);
        Assert.Equal(50, config.MaxActive);
    }

    [Fact]
    public void StateConfig_InheritsBrokerAndAddsExpiry()
    {
        var config = new StateConfig();

        Assert.Equal(3000, config.ExpirySeconds);
        Assert.Equal(3600, config.MetaExpirySeconds);
        Assert.Equal("redis", config.Type); // inherited
    }

    [Fact]
    public void ResultDbConfig_InheritsDbPoolAndAddsFields()
    {
        var config = new ResultDbConfig();

        Assert.Equal("results_{0}", config.ResultsTable);
        Assert.False(config.Unlogged);
        Assert.Equal(10, config.MaxIdle); // inherited
        Assert.Equal(100, config.MaxActive); // inherited
    }
}
