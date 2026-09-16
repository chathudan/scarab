using System.Text.Json.Serialization;

namespace Scarab.Models;

public class GroupStatusResp
{
    [JsonPropertyName("group_id")]
    public string GroupId { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("jobs")]
    public List<JobStatusResp> Jobs { get; set; } = new();
}
