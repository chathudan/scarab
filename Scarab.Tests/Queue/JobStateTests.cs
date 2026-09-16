using Scarab.Queue;

namespace Scarab.Tests.Queue;

public class JobStateTests
{
    [Fact]
    public void JobState_DefaultStatus_IsEmpty()
    {
        var state = new JobState { JobId = "j1" };
        Assert.Equal("j1", state.JobId);
        Assert.Equal("", state.Status);
        Assert.Equal(0, state.Result);
    }

    [Fact]
    public void GroupState_Defaults()
    {
        var state = new GroupState { GroupId = "g1" };
        Assert.Equal("g1", state.GroupId);
        Assert.Equal("", state.Status);
        Assert.Empty(state.JobIds);
    }
}
