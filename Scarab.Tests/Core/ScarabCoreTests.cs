using NSubstitute;
using Scarab.Config;
using Scarab.Core;
using Scarab.Models;
using Scarab.Queue;
using Scarab.ResultBackends;
using Microsoft.Extensions.Logging.Abstractions;

namespace Scarab.Tests.Core;

public class ScarabCoreTests
{
    private readonly IJobBroker _broker = Substitute.For<IJobBroker>();
    private readonly IJobStateStore _stateStore = Substitute.For<IJobStateStore>();
    private readonly AppConfig _appConfig = new() { Queue = "default", DefaultJobTtlSeconds = 60, WorkerConcurrency = 5 };

    private ScarabCore CreateCore()
    {
        var srcDbs = new Scarab.DbPool.DbPool();
        var resultBackends = new ResultBackendCollection();
        return new ScarabCore(_appConfig, srcDbs, resultBackends, _broker, _stateStore, new NullLogger<ScarabCore>());
    }

    [Fact]
    public async Task NewJob_UnknownTask_Throws()
    {
        var core = CreateCore();
        core.Tasks = new TaskCollection();

        await Assert.ThrowsAsync<InvalidOperationException>(() => core.NewJob(new JobReq(), "nonexistent"));
    }

    [Fact]
    public async Task NewJob_ValidTask_EnqueuesAndReturnsResponse()
    {
        var core = CreateCore();
        var srcDbs = new Scarab.DbPool.DbPool();
        srcDbs["db1"] = new Scarab.DbPool.DbConnectionFactory("db1", new DbPoolConfig { Type = "mssql", Dsn = "Server=localhost;" });

        core.Tasks = new TaskCollection
        {
            ["my_task"] = new SqlTask
            {
                Name = "my_task",
                Queue = "default",
                RawSql = "SELECT 1",
                SourceDbs = srcDbs,
                ResultBackends = new ResultBackendCollection()
            }
        };

        var req = new JobReq { Args = ["arg1"], Retries = 2 };
        var resp = await core.NewJob(req, "my_task");

        Assert.Equal("my_task", resp.TaskName);
        Assert.Equal("default", resp.Queue);
        Assert.Equal(2, resp.Retries);
        Assert.NotEmpty(resp.JobId);

        await _broker.Received(1).Enqueue("default", Arg.Any<JobMessage>());
        await _stateStore.Received(1).SetJobState(Arg.Any<string>(), "pending", "my_task", "default", null, null);
    }

    [Fact]
    public async Task NewJob_WithEtaInFuture_EnqueuesDelayed()
    {
        var core = CreateCore();
        var srcDbs = new Scarab.DbPool.DbPool();
        srcDbs["db1"] = new Scarab.DbPool.DbConnectionFactory("db1", new DbPoolConfig { Type = "postgres", Dsn = "Host=localhost;" });

        core.Tasks = new TaskCollection
        {
            ["delayed_task"] = new SqlTask
            {
                Name = "delayed_task",
                Queue = "default",
                RawSql = "SELECT 1",
                SourceDbs = srcDbs,
                ResultBackends = new ResultBackendCollection()
            }
        };

        var futureDate = DateTime.UtcNow.AddHours(1).ToString("O");
        var req = new JobReq { Eta = futureDate };
        await core.NewJob(req, "delayed_task");

        await _broker.Received(1).EnqueueDelayed("default", Arg.Any<JobMessage>(), Arg.Any<DateTime>());
        await _broker.DidNotReceive().Enqueue(Arg.Any<string>(), Arg.Any<JobMessage>());
    }

    [Fact]
    public async Task NewJob_InvalidJobId_Throws()
    {
        var core = CreateCore();
        var srcDbs = new Scarab.DbPool.DbPool();
        srcDbs["db1"] = new Scarab.DbPool.DbConnectionFactory("db1", new DbPoolConfig { Type = "mssql", Dsn = "Server=localhost;" });

        core.Tasks = new TaskCollection
        {
            ["t"] = new SqlTask { Name = "t", Queue = "default", RawSql = "SELECT 1", SourceDbs = srcDbs, ResultBackends = new ResultBackendCollection() }
        };

        var req = new JobReq { JobId = "invalid job id!!!" };
        await Assert.ThrowsAsync<ArgumentException>(() => core.NewJob(req, "t"));
    }

    [Fact]
    public async Task GetJobStatus_NotFound_Throws()
    {
        var core = CreateCore();
        _stateStore.GetJobState("missing").Returns((JobState?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => core.GetJobStatus("missing"));
    }

    [Fact]
    public async Task GetJobStatus_ExistingJob_ReturnsMappedState()
    {
        var core = CreateCore();
        _stateStore.GetJobState("j1").Returns(new JobState { JobId = "j1", Status = "done", Result = 42 });

        var resp = await core.GetJobStatus("j1");

        Assert.Equal("j1", resp.JobId);
        Assert.Equal("SUCCESS", resp.State);
        Assert.Equal(42, resp.Count);
    }

    [Fact]
    public async Task CancelJob_NotFound_Throws()
    {
        var core = CreateCore();
        _stateStore.GetJobState("ghost").Returns((JobState?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => core.CancelJob("ghost", false));
    }

    [Fact]
    public async Task CancelJob_CompletedNoPurge_Throws()
    {
        var core = CreateCore();
        _stateStore.GetJobState("done_job").Returns(new JobState { JobId = "done_job", Status = "done" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => core.CancelJob("done_job", false));
    }

    [Fact]
    public async Task CancelJob_CompletedWithPurge_Deletes()
    {
        var core = CreateCore();
        _stateStore.GetJobState("done_job").Returns(new JobState { JobId = "done_job", Status = "done" });

        await core.CancelJob("done_job", true);

        await _stateStore.Received(1).DeleteJob("done_job");
    }

    [Fact]
    public async Task GetPendingJobs_DelegatesToBroker()
    {
        var core = CreateCore();
        var expected = new List<JobMessage> { new() { JobId = "j1" } };
        _broker.GetPending("myq").Returns(expected);

        var result = await core.GetPendingJobs("myq");

        Assert.Single(result);
        Assert.Equal("j1", result[0].JobId);
    }

    [Fact]
    public async Task NewJobGroup_EmptyTask_Throws()
    {
        var core = CreateCore();
        core.Tasks = new TaskCollection();

        var req = new GroupReq { Jobs = [new JobReq { TaskName = "missing" }] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => core.NewJobGroup(req));
    }

    [Fact]
    public async Task GetGroupStatus_NotFound_Throws()
    {
        var core = CreateCore();
        _stateStore.GetGroupState("missing").Returns((GroupState?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => core.GetJobGroupStatus("missing"));
    }
}
