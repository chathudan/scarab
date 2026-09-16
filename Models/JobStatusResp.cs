using System.Text.Json.Serialization;

namespace Scarab.Models;

public class JobStatusResp
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}
