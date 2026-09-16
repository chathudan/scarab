namespace Scarab.DbPool;

public class DbPool : Dictionary<string, DbConnectionFactory>
{
    private static readonly Random _rng = new();

    public DbConnectionFactory Get(string name)
    {
        if (TryGetValue(name, out var factory))
            return factory;
        throw new KeyNotFoundException($"Database '{name}' not found in pool. Available: {string.Join(", ", Keys)}");
    }

    public (string name, DbConnectionFactory factory) GetRandom()
    {
        if (Count == 0) throw new InvalidOperationException("DB pool is empty");
        var keys = Keys.ToArray();
        var key = keys[_rng.Next(keys.Length)];
        return (key, this[key]);
    }

    public string[] GetNames() => Keys.ToArray();

    public DbPool FilterByNames(string[] names)
    {
        var filtered = new DbPool();
        foreach (var name in names)
        {
            var trimmed = name.Trim();
            if (!TryGetValue(trimmed, out var factory))
                throw new KeyNotFoundException($"Database '{trimmed}' not found in pool. Available: {string.Join(", ", Keys)}");
            filtered[trimmed] = factory;
        }
        return filtered;
    }

    /// <summary>
    /// Like <see cref="FilterByNames"/> but doesn't throw for names that aren't in the pool
    /// (e.g. an optional demo DB that failed its startup ping and was skipped) - those are
    /// reported via <paramref name="missing"/> instead, so a task referencing one doesn't
    /// take down startup for tasks that don't need it.
    /// </summary>
    public DbPool FilterByKnownNames(string[] names, out string[] missing)
    {
        var filtered = new DbPool();
        var missingList = new List<string>();
        foreach (var name in names)
        {
            var trimmed = name.Trim();
            if (TryGetValue(trimmed, out var factory))
                filtered[trimmed] = factory;
            else
                missingList.Add(trimmed);
        }
        missing = missingList.ToArray();
        return filtered;
    }
}
