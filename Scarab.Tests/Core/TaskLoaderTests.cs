using Scarab.Core;
using Scarab.Config;

namespace Scarab.Tests.Core;

public class TaskLoaderTests
{
    [Fact]
    public void ParseSqlFile_BasicQuery_ParsesNameAndBody()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "test.sql");
        File.WriteAllText(file, """
            -- name: get_users
            SELECT * FROM users WHERE id = @p1;
            """);

        var loader = new TaskLoader(new Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskLoader>());
        var backends = new Scarab.ResultBackends.ResultBackendCollection();
        var dbPool = new Scarab.DbPool.DbPool();
        var appConfig = new AppConfig();

        var tasks = loader.LoadTasks([dir], dbPool, backends, appConfig);

        Assert.Single(tasks);
        Assert.True(tasks.ContainsKey("get_users"));
        Assert.Contains("SELECT * FROM users", tasks["get_users"].RawSql);

        Directory.Delete(dir, true);
    }

    [Fact]
    public void ParseSqlFile_WithTags_ParsesQueueAndDb()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test.sql"), """
            -- name: get_report
            -- queue: high_priority
            -- raw: 1
            -- conc: 5
            SELECT 1;
            """);

        var loader = new TaskLoader(new Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskLoader>());
        var tasks = loader.LoadTasks([dir], new Scarab.DbPool.DbPool(), new Scarab.ResultBackends.ResultBackendCollection(), new AppConfig());

        var task = tasks["get_report"];
        Assert.Equal("high_priority", task.Queue);
        Assert.False(task.IsPrepared); // raw:1
        Assert.Equal(5, task.Concurrency);

        Directory.Delete(dir, true);
    }

    [Fact]
    public void ParseSqlFile_WithParamsTag_ParsesParamNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test.sql"), """
            -- name: filtered_report
            -- params: status, category
            SELECT 1 WHERE status = ? AND category = ?;
            """);

        var loader = new TaskLoader(new Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskLoader>());
        var tasks = loader.LoadTasks([dir], new Scarab.DbPool.DbPool(), new Scarab.ResultBackends.ResultBackendCollection(), new AppConfig());

        Assert.Equal(["status", "category"], tasks["filtered_report"].Params);

        Directory.Delete(dir, true);
    }

    [Fact]
    public void ParseSqlFile_NoParamsTag_ParamsIsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test.sql"), "-- name: no_params\nSELECT 1;");

        var loader = new TaskLoader(new Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskLoader>());
        var tasks = loader.LoadTasks([dir], new Scarab.DbPool.DbPool(), new Scarab.ResultBackends.ResultBackendCollection(), new AppConfig());

        Assert.Empty(tasks["no_params"].Params);

        Directory.Delete(dir, true);
    }

    [Fact]
    public void ParseSqlFile_MultipleQueries_ParsesAll()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "multi.sql"), """
            -- name: query_one
            SELECT 1;

            -- name: query_two
            SELECT 2;

            -- name: query_three
            -- queue: batch
            SELECT 3;
            """);

        var loader = new TaskLoader(new Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskLoader>());
        var tasks = loader.LoadTasks([dir], new Scarab.DbPool.DbPool(), new Scarab.ResultBackends.ResultBackendCollection(), new AppConfig());

        Assert.Equal(3, tasks.Count);
        Assert.True(tasks.ContainsKey("query_one"));
        Assert.True(tasks.ContainsKey("query_two"));
        Assert.True(tasks.ContainsKey("query_three"));
        Assert.Equal("batch", tasks["query_three"].Queue);

        Directory.Delete(dir, true);
    }

    [Fact]
    public void ParseSqlFile_DuplicateName_Throws()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.sql"), "-- name: dup_task\nSELECT 1;");
        File.WriteAllText(Path.Combine(dir, "b.sql"), "-- name: dup_task\nSELECT 2;");

        var loader = new TaskLoader(new Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskLoader>());

        Assert.Throws<InvalidOperationException>(() =>
            loader.LoadTasks([dir], new Scarab.DbPool.DbPool(), new Scarab.ResultBackends.ResultBackendCollection(), new AppConfig()));

        Directory.Delete(dir, true);
    }

    [Fact]
    public void ParseSqlFile_MissingDirectory_NoException()
    {
        var loader = new TaskLoader(new Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskLoader>());
        var tasks = loader.LoadTasks(["/nonexistent/path"], new Scarab.DbPool.DbPool(), new Scarab.ResultBackends.ResultBackendCollection(), new AppConfig());

        Assert.Empty(tasks);
    }
}
