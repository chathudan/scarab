namespace Scarab.ResultBackends;

public class ResultBackendCollection : Dictionary<string, IResultBackend>
{
    private static readonly Random _rng = new();

    public (string name, IResultBackend backend) GetRandom()
    {
        if (Count == 0) throw new InvalidOperationException("No result backends configured");
        var keys = Keys.ToArray();
        var key = keys[_rng.Next(keys.Length)];
        return (key, this[key]);
    }

    public string[] GetNames() => Keys.ToArray();

    public ResultBackendCollection FilterByNames(string[] names)
    {
        var filtered = new ResultBackendCollection();
        foreach (var name in names)
        {
            var trimmed = name.Trim();
            if (!TryGetValue(trimmed, out var backend))
                throw new KeyNotFoundException($"Result backend '{trimmed}' not found. Available: {string.Join(", ", Keys)}");
            filtered[trimmed] = backend;
        }
        return filtered;
    }
}
