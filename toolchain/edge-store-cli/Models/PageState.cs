using System.Text.Json.Serialization;

namespace Vainreef.EdgeStore.State;

public enum PartnerPageKind
{
    Unknown,
    LoadingShell,
    SignIn,
    ProductOverview,
    SubmissionOverview,
    AvailabilityForm,
    PropertiesForm,
    AgeRatingsQuestionnaire,
    AgeRatingsSummary,
    PackagesForm,
    ListingLanguageGrid,
    ListingForm,
    OptionsForm,
    SubmissionConfirmation,
    CertificationStatus,
    ErrorPage
}

public enum ModuleCompletion
{
    Unknown,
    Incomplete,
    Processing,
    Complete,
    Error
}

public sealed class PageSnapshot
{
    [JsonPropertyName("capturedAt")]
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("kind")]
    public PartnerPageKind Kind { get; set; }

    [JsonPropertyName("ready")]
    public bool Ready { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("heading")]
    public string Heading { get; set; } = string.Empty;

    [JsonPropertyName("textPreview")]
    public string TextPreview { get; set; } = string.Empty;

    [JsonPropertyName("signals")]
    public Dictionary<string, bool> Signals { get; set; } = [];

    [JsonPropertyName("buttons")]
    public List<string> Buttons { get; set; } = [];

    [JsonPropertyName("visibleErrors")]
    public List<string> VisibleErrors { get; set; } = [];

    [JsonPropertyName("modules")]
    public Dictionary<string, ModuleCompletion> Modules { get; set; } = [];
}

public sealed class PackageEntry
{
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsValidated => Status.Contains("Validated", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("已验证", StringComparison.OrdinalIgnoreCase);
    public bool IsError => Status.Contains("Error", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("错误", StringComparison.OrdinalIgnoreCase);
    public bool IsProcessing => !IsValidated && !IsError;
}
