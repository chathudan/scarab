using Scarab.Config;
using Scarab.DbPool;

namespace Scarab.Tests.DbPoolTests;

public class DbPoolTests
{
    [Fact]
    public void Get_ExistingKey_ReturnsFactory()
    {
        var pool = new Scarab.DbPool.DbPool();
        var factory = new DbConnectionFactory("db1", new DbPoolConfig { Type = "postgres", Dsn = "Host=localhost;" });
        pool["db1"] = factory;

        Assert.Same(factory, pool.Get("db1"));
    }

    [Fact]
    public void Get_MissingKey_Throws()
    {
        var pool = new Scarab.DbPool.DbPool();
        Assert.Throws<KeyNotFoundException>(() => pool.Get("missing"));
    }

    [Fact]
    public void GetRandom_EmptyPool_Throws()
    {
        var pool = new Scarab.DbPool.DbPool();
        Assert.Throws<InvalidOperationException>(() => pool.GetRandom());
    }

    [Fact]
    public void GetRandom_SingleItem_ReturnsThatItem()
    {
        var pool = new Scarab.DbPool.DbPool();
        var factory = new DbConnectionFactory("only", new DbPoolConfig { Type = "mysql", Dsn = "Server=localhost;" });
        pool["only"] = factory;

        var (name, f) = pool.GetRandom();
        Assert.Equal("only", name);
        Assert.Same(factory, f);
    }

    [Fact]
    public void FilterByNames_ValidNames_ReturnsSubset()
    {
        var pool = new Scarab.DbPool.DbPool();
        pool["a"] = new DbConnectionFactory("a", new DbPoolConfig { Type = "mssql", Dsn = "Server=localhost;" });
        pool["b"] = new DbConnectionFactory("b", new DbPoolConfig { Type = "mysql", Dsn = "Server=localhost;" });
        pool["c"] = new DbConnectionFactory("c", new DbPoolConfig { Type = "postgres", Dsn = "Host=localhost;" });

        var filtered = pool.FilterByNames(["a", "c"]);

        Assert.Equal(2, filtered.Count);
        Assert.True(filtered.ContainsKey("a"));
        Assert.True(filtered.ContainsKey("c"));
        Assert.False(filtered.ContainsKey("b"));
    }

    [Fact]
    public void FilterByNames_InvalidName_Throws()
    {
        var pool = new Scarab.DbPool.DbPool();
        pool["a"] = new DbConnectionFactory("a", new DbPoolConfig { Type = "mssql", Dsn = "Server=localhost;" });

        Assert.Throws<KeyNotFoundException>(() => pool.FilterByNames(["a", "missing"]));
    }

    [Fact]
    public void GetNames_ReturnsAllKeys()
    {
        var pool = new Scarab.DbPool.DbPool();
        pool["x"] = new DbConnectionFactory("x", new DbPoolConfig { Type = "mssql", Dsn = "Server=localhost;" });
        pool["y"] = new DbConnectionFactory("y", new DbPoolConfig { Type = "mysql", Dsn = "Server=localhost;" });

        var names = pool.GetNames();
        Assert.Equal(2, names.Length);
        Assert.Contains("x", names);
        Assert.Contains("y", names);
    }
}
