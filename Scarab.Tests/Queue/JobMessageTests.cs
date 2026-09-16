using System.Text.Json;
using Scarab.Queue;

namespace Scarab.Tests.Queue;

public class JobMessageTests
{
    [Fact]
    public void Serialization_RoundTrips()
    {
        var msg = new JobMessage
        {
            JobId = "test-123",
            TaskName = "my_task",
            Queue = "default",
            Args = ["arg1", "arg2"],
            Db = "pg_db",
            TtlSeconds = 300,
            MaxRetries = 3,
            Retries = 1,
            Eta = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            GroupId = "grp-1"
        };

        var json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<JobMessage>(json)!;

        Assert.Equal("test-123", deserialized.JobId);
        Assert.Equal("my_task", deserialized.TaskName);
        Assert.Equal("default", deserialized.Queue);
        Assert.Equal(2, deserialized.Args!.Length);
        Assert.Equal("pg_db", deserialized.Db);
        Assert.Equal(300, deserialized.TtlSeconds);
        Assert.Equal(3, deserialized.MaxRetries);
        Assert.Equal(1, deserialized.Retries);
        Assert.NotNull(deserialized.Eta);
        Assert.Equal("grp-1", deserialized.GroupId);
    }

    [Fact]
    public void Deserialization_MissingOptionals_DefaultsToNull()
    {
        var json = """{"job_id":"j1","task":"t","queue":"q"}""";
        var msg = JsonSerializer.Deserialize<JobMessage>(json)!;

        Assert.Equal("j1", msg.JobId);
        Assert.Null(msg.Args);
        Assert.Null(msg.Db);
        Assert.Null(msg.Eta);
        Assert.Null(msg.GroupId);
        Assert.Equal(0, msg.TtlSeconds);
    }
}
