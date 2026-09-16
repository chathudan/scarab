using Scarab.ResultBackends;

namespace Scarab.Tests.ResultBackends;

public class ResultBackendCollectionTests
{
    [Fact]
    public void GetRandom_Empty_Throws()
    {
        var col = new ResultBackendCollection();
        Assert.Throws<InvalidOperationException>(() => col.GetRandom());
    }

    [Fact]
    public void FilterByNames_InvalidName_Throws()
    {
        var col = new ResultBackendCollection();
        Assert.Throws<KeyNotFoundException>(() => col.FilterByNames(["missing"]));
    }

    [Fact]
    public void GetNames_ReturnsKeys()
    {
        var col = new ResultBackendCollection();
        var mock = NSubstitute.Substitute.For<IResultBackend>();
        col["a"] = mock;
        col["b"] = mock;

        var names = col.GetNames();
        Assert.Equal(2, names.Length);
        Assert.Contains("a", names);
        Assert.Contains("b", names);
    }

    [Fact]
    public void FilterByNames_ValidNames_ReturnsSubset()
    {
        var col = new ResultBackendCollection();
        var mock = NSubstitute.Substitute.For<IResultBackend>();
        col["x"] = mock;
        col["y"] = mock;
        col["z"] = mock;

        var filtered = col.FilterByNames(["x", "z"]);
        Assert.Equal(2, filtered.Count);
        Assert.True(filtered.ContainsKey("x"));
        Assert.True(filtered.ContainsKey("z"));
    }
}
