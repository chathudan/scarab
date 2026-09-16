using System.Text.Json.Serialization;

namespace Scarab.Models;

public class GroupResp
{
    [JsonPropertyName("group_id")]
    public string GroupId { get; set; } = "";

    [JsonPropertyName("jobs")]
    public List<JobResp> Jobs { get; set; } = new();
}
