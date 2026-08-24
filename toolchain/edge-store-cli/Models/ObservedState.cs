using System.Text.Json.Serialization;

namespace Vainreef.EdgeStore.State;

public class ObservedState
{
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("submissionId")]
    public string SubmissionId { get; set; } = string.Empty;

    [JsonPropertyName("discoveredHrefs")]
    public Dictionary<string, string> DiscoveredHrefs { get; set; } = [];

    [JsonPropertyName("availability")]
    public ObservedAvailability Availability { get; set; } = new();

    [JsonPropertyName("properties")]
    public ObservedProperties Properties { get; set; } = new();

    [JsonPropertyName("ageRatings")]
    public ObservedAgeRatings AgeRatings { get; set; } = new();

    [JsonPropertyName("packages")]
    public ObservedPackages Packages { get; set; } = new();

    [JsonPropertyName("listing")]
    public ObservedListing Listing { get; set; } = new();

    [JsonPropertyName("options")]
    public ObservedOptions Options { get; set; } = new();

    [JsonPropertyName("visibleErrors")]
    public List<string> VisibleErrors { get; set; } = [];

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ObservedAvailability
{
    public bool AllMarkets { get; set; }
    public string Audience { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string PriceTier { get; set; } = string.Empty;
    public string ReleaseSchedule { get; set; } = string.Empty;
    public string StopSelling { get; set; } = string.Empty;
    public bool SaveButtonEnabled { get; set; }
}

public class ObservedProperties
{
    public string Category { get; set; } = string.Empty;
    public string PrivacyAnswer { get; set; } = string.Empty;
    public string PrivacyPolicyText { get; set; } = string.Empty;
    public bool StorageDeclaration { get; set; }
    public bool BackupsDeclaration { get; set; }
    public bool WindowsDeclaration { get; set; }
    public bool UsesGenAi { get; set; }
}

public class ObservedAgeRatings
{
    public string InputMode { get; set; } = string.Empty;
    public string ApplicationType { get; set; } = string.Empty;
    public bool QuestionnaireCompleted { get; set; }
    public bool TermsAgreed { get; set; }
    public bool IsCompleted { get; set; }
}

public class ObservedPackages
{
    public List<string> UploadedPackageNames { get; set; } = [];
    public bool DesktopFamily { get; set; }
    public bool MobileFamily { get; set; }
    public bool XboxFamily { get; set; }
    public bool TeamFamily { get; set; }
    public bool MixedRealityFamily { get; set; }
    public bool FutureDeviceFamilies { get; set; }
}

public class ObservedListing
{
    public string ReservedTitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public List<string> Features { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
    public bool HasScreenshot { get; set; }
    public bool HasLogo300 { get; set; }
    public bool HasLogo150 { get; set; }
    public bool HasBoxart { get; set; }
}

public class ObservedOptions
{
    public string PublishMode { get; set; } = string.Empty;
    public bool HasFullTrustBox { get; set; }
    public string FullTrustReasonText { get; set; } = string.Empty;
}
