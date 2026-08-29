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
    public int SchemaVersion { get; set; } = 4;

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

    [JsonPropertyName("phaseEvidence")]
    public Dictionary<string, PhaseEvidence> PhaseEvidence { get; set; } = [];

    public void MarkConverged(string phase, string overviewEvidence, string overviewUrl)
    {
        if (string.IsNullOrWhiteSpace(overviewEvidence) || string.IsNullOrWhiteSpace(overviewUrl))
            throw new ArgumentException("Convergence requires overview evidence and URL.");
        PhaseStatuses[phase] = PhaseStatus.Converged;
        if (!ConvergedPhases.Contains(phase))
        {
            ConvergedPhases.Add(phase);
        }
        PhaseEvidence[phase] = new PhaseEvidence
        {
            Status = PhaseStatus.Converged,
            Detail = overviewEvidence,
            Url = overviewUrl,
            VerifiedAt = DateTimeOffset.UtcNow
        };
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Mark(string phase, PhaseStatus status, string detail = "", string url = "")
    {
        PhaseStatuses[phase] = status;
        if (status != PhaseStatus.Converged) ConvergedPhases.Remove(phase);
        PhaseEvidence[phase] = new PhaseEvidence
        {
            Status = status,
            Detail = detail,
            Url = url,
            VerifiedAt = DateTimeOffset.UtcNow
        };
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsConverged(string phase) => ConvergedPhases.Contains(phase);
}

public sealed class PhaseEvidence
{
    public PhaseStatus Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset VerifiedAt { get; set; }
}
