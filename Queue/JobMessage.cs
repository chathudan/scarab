using System.Text.Json.Serialization;

namespace Scarab.Queue;

public class JobMessage
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = "";

    [JsonPropertyName("task")]
    public string TaskName { get; set; } = "";

    [JsonPropertyName("queue")]
    public string Queue { get; set; } = "";

    [JsonPropertyName("args")]
    public object[]? Args { get; set; }

    [JsonPropertyName("db")]
    public string? Db { get; set; }

    [JsonPropertyName("ttl")]
    public int TtlSeconds { get; set; }

    [JsonPropertyName("retries")]
    public int Retries { get; set; }

    [JsonPropertyName("max_retries")]
    public int MaxRetries { get; set; }

    [JsonPropertyName("eta")]
    public DateTime? Eta { get; set; }

    [JsonPropertyName("group_id")]
    public string? GroupId { get; set; }
}
