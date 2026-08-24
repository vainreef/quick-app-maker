using System.Text.Json.Serialization;

namespace Vainreef.EdgeStore.State;

public enum PhaseStatus
{
    Unknown,
    Observed,
    NeedsChanges,
    Applying,
    AppliedUnverified,
    Converged,
    Failed
}

public class StoreCheckpoint
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 3;

    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("submissionId")]
    public string SubmissionId { get; set; } = string.Empty;

    [JsonPropertyName("phaseStatuses")]
    public Dictionary<string, PhaseStatus> PhaseStatuses { get; set; } = [];

    [JsonPropertyName("convergedPhases")]
    public List<string> ConvergedPhases { get; set; } = [];

    [JsonPropertyName("lastUrl")]
    public string LastUrl { get; set; } = string.Empty;

    [JsonPropertyName("lastTitle")]
    public string LastTitle { get; set; } = string.Empty;

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void MarkConverged(string phase)
    {
        PhaseStatuses[phase] = PhaseStatus.Converged;
        if (!ConvergedPhases.Contains(phase))
        {
            ConvergedPhases.Add(phase);
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsConverged(string phase) => ConvergedPhases.Contains(phase);
}
