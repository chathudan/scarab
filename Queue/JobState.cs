namespace Scarab.Queue;

public class JobState
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? TaskName { get; set; }
    public string? Queue { get; set; }
    public string? Error { get; set; }
    public int Result { get; set; }
    public string? GroupId { get; set; }
}

public class GroupState
{
    public string GroupId { get; set; } = "";
    public string Status { get; set; } = "";
    public string[] JobIds { get; set; } = [];
}
