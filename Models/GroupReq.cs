using System.Text.Json.Serialization;

namespace Scarab.Models;

public class GroupReq
{
    [JsonPropertyName("group_id")]
    public string? GroupId { get; set; }

    [JsonPropertyName("concurrency")]
    public int Concurrency { get; set; }

    [JsonPropertyName("jobs")]
    public List<JobReq> Jobs { get; set; } = new();
}
