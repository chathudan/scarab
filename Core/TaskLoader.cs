using Scarab.Config;
using Scarab.ResultBackends;

namespace Scarab.Core;

public class TaskLoader
{
    private readonly ILogger<TaskLoader> _logger;

    public TaskLoader(ILogger<TaskLoader> logger)
    {
        _logger = logger;
    }

    public TaskCollection LoadTasks(string[] directories, DbPool.DbPool srcDbs, ResultBackendCollection resultBackends, AppConfig appConfig)
    {
        var tasks = new TaskCollection();

        foreach (var dir in directories)
        {
            var fullPath = Path.GetFullPath(dir);
            if (!Directory.Exists(fullPath))
            {
                _logger.LogWarning("SQL directory not found: {Dir}", fullPath);
                continue;
            }

            foreach (var file in Directory.GetFiles(fullPath, "*.sql", SearchOption.TopDirectoryOnly))
            {
                _logger.LogInformation("Parsing SQL file: {File}", file);
                var parsed = ParseSqlFile(file);

                foreach (var (name, query) in parsed)
                {
                    if (tasks.ContainsKey(name))
                        throw new InvalidOperationException($"Duplicate task name '{name}' found in {file}");

                    var task = new SqlTask { Name = name, RawSql = query.Query };

                    // Queue
                    if (query.Tags.TryGetValue("queue", out var queue) && !string.IsNullOrWhiteSpace(queue))
                        task.Queue = queue.Trim();
                    else
                        task.Queue = appConfig.Queue;

                    // Raw
                    if (query.Tags.TryGetValue("raw", out var raw) && raw.Trim() == "1")
                        task.IsPrepared = false;

                    // Concurrency
                    if (query.Tags.TryGetValue("conc", out var conc) && int.TryParse(conc.Trim(), out var concVal))
                        task.Concurrency = concVal;
                    else
                        task.Concurrency = appConfig.WorkerConcurrency;

                    // Source DBs. Missing names are dropped rather than failing the whole
                    // startup - e.g. a task tagged for an optional demo DB that didn't
                    // come up shouldn't take every other task down with it.
                    if (query.Tags.TryGetValue("db", out var dbTag) && !string.IsNullOrWhiteSpace(dbTag))
                    {
                        task.SourceDbs = srcDbs.FilterByKnownNames(dbTag.Split(','), out var missingDbs);
                        if (missingDbs.Length > 0)
                            _logger.LogWarning("Task '{Name}' references source DB(s) not currently in the pool: {Missing}. It will fail at execution time until they're available.", name, string.Join(",", missingDbs));
                    }
                    else
                        task.SourceDbs = srcDbs;

                    // Params (documentation only - names the positional args this task's SQL expects)
                    if (query.Tags.TryGetValue("params", out var paramsTag) && !string.IsNullOrWhiteSpace(paramsTag))
                        task.Params = paramsTag.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();

                    // Result backends
                    if (query.Tags.TryGetValue("results", out var resTag) && !string.IsNullOrWhiteSpace(resTag))
                        task.ResultBackends = resultBackends.FilterByNames(resTag.Split(','));
                    else
                        task.ResultBackends = resultBackends;

                    tasks[name] = task;
                    _logger.LogInformation("Loaded task: {Name} (queue={Queue}, dbs={Dbs}, results={Results}, raw={Raw})", name, task.Queue, string.Join(",", task.SourceDbs.GetNames()), string.Join(",", task.ResultBackends.GetNames()), !task.IsPrepared);
                }
            }
        }

        _logger.LogInformation("Loaded {Count} tasks", tasks.Count);
        return tasks;
    }

    private Dictionary<string, ParsedQuery> ParseSqlFile(string filePath)
    {
        var queries = new Dictionary<string, ParsedQuery>();
        var lines = File.ReadAllLines(filePath);

        string? currentName = null;
        var tags = new Dictionary<string, string>();
        var sqlLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("-- name:"))
            {
                // Flush previous
                if (currentName != null)
                {
                    queries[currentName] = new ParsedQuery
                    {
                        Name = currentName,
                        Query = string.Join("\n", sqlLines).Trim(),
                        Tags = new Dictionary<string, string>(tags)
                    };
                }

                currentName = trimmed["-- name:".Length..].Trim();
                tags.Clear();
                sqlLines.Clear();
            }
            else if (currentName != null && trimmed.StartsWith("-- ") && trimmed.Contains(':') && sqlLines.Count == 0)
            {
                // Tag line (only before SQL body starts)
                var tagContent = trimmed[3..]; // skip "-- "
                var colonIdx = tagContent.IndexOf(':');
                if (colonIdx > 0)
                {
                    var key = tagContent[..colonIdx].Trim();
                    var val = tagContent[(colonIdx + 1)..].Trim();
                    tags[key] = val;
                }
            }
            else if (currentName != null && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("--"))
            {
                sqlLines.Add(line);
            }
            else if (currentName != null && sqlLines.Count > 0)
            {
                // Preserve empty lines within SQL body
                sqlLines.Add(line);
            }
        }

        // Flush last
        if (currentName != null)
        {
            queries[currentName] = new ParsedQuery
            {
                Name = currentName,
                Query = string.Join("\n", sqlLines).Trim(),
                Tags = new Dictionary<string, string>(tags)
            };
        }

        return queries;
    }
}

internal class ParsedQuery
{
    public string Name { get; set; } = "";
    public string Query { get; set; } = "";
    public Dictionary<string, string> Tags { get; set; } = new();
}
