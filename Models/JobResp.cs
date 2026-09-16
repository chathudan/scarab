using System.Text.Json.Serialization;

namespace Scarab.Models;

public class JobResp
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = "";

    [JsonPropertyName("task")]
    public string TaskName { get; set; } = "";

    [JsonPropertyName("queue")]
    public string Queue { get; set; } = "";

    [JsonPropertyName("eta")]
    public DateTime? Eta { get; set; }

    [JsonPropertyName("retries")]
    public int Retries { get; set; }
}
