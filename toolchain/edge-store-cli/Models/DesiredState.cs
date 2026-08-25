using System.Text.Json.Serialization;

namespace Vainreef.EdgeStore.State;

public class DesiredState
{
    [JsonPropertyName("productId")]
    public string ProductId { get; set; } = string.Empty;

    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("submissionId")]
    public string SubmissionId { get; set; } = string.Empty;

    [JsonPropertyName("site")]
    public SiteConfig Site { get; set; } = new();

    [JsonPropertyName("listingMarkdown")]
    public string ListingMarkdown { get; set; } = string.Empty;

    [JsonPropertyName("values")]
    public ValuesConfig Values { get; set; } = new();

    [JsonPropertyName("properties")]
    public PropertiesConfig Properties { get; set; } = new();

    [JsonPropertyName("pricing")]
    public PricingConfig Pricing { get; set; } = new();

    [JsonPropertyName("assets")]
    public AssetsConfig Assets { get; set; } = new();

    [JsonPropertyName("listing")]
    public ListingToggles Listing { get; set; } = new();

    [JsonPropertyName("submissionOptions")]
    public SubmissionOptionsConfig SubmissionOptions { get; set; } = new();
}

public class SiteConfig
{
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = "https://partner.microsoft.com/zh-cn/dashboard/products";

    [JsonPropertyName("languageId")]
    public string LanguageId { get; set; } = "5";

    [JsonPropertyName("languageCode")]
    public string LanguageCode { get; set; } = "zh-cn";

    [JsonPropertyName("supportedLanguageCodes")]
    public List<string> SupportedLanguageCodes { get; set; } = [];
}

public class ValuesConfig
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("shortDescription")]
    public string ShortDescription { get; set; } = string.Empty;

    [JsonPropertyName("features")]
    public List<string> Features { get; set; } = [];

    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; set; } = [];
}

public class PropertiesConfig
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "Productivity";

    [JsonPropertyName("privacy")]
    public string Privacy { get; set; } = "No";

    [JsonPropertyName("privacyPolicyText")]
    public string PrivacyPolicyText { get; set; } = string.Empty;

    [JsonPropertyName("privacyPolicyUrl")]
    public string PrivacyPolicyUrl { get; set; } = string.Empty;
}

public class PricingConfig
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "CN";

    [JsonPropertyName("priceTier")]
    public string PriceTier { get; set; } = "0";
}

public class AssetsConfig
{
    [JsonPropertyName("msix")]
    public string Msix { get; set; } = string.Empty;

    [JsonPropertyName("screenshot")]
    public string Screenshot { get; set; } = string.Empty;

    [JsonPropertyName("poster")]
    public string Poster { get; set; } = string.Empty;

    [JsonPropertyName("boxart")]
    public string Boxart { get; set; } = string.Empty;

    [JsonPropertyName("logo300")]
    public string Logo300 { get; set; } = string.Empty;

    [JsonPropertyName("logo150")]
    public string Logo150 { get; set; } = string.Empty;

    [JsonPropertyName("logo71")]
    public string Logo71 { get; set; } = string.Empty;

    [JsonPropertyName("superhero")]
    public string Superhero { get; set; } = string.Empty;
}

public class ListingToggles
{
    [JsonPropertyName("screenshot")]
    public bool Screenshot { get; set; } = true;

    [JsonPropertyName("poster")]
    public bool Poster { get; set; } = false;

    [JsonPropertyName("boxart")]
    public bool Boxart { get; set; } = true;

    [JsonPropertyName("logo300")]
    public bool Logo300 { get; set; } = true;

    [JsonPropertyName("logo150")]
    public bool Logo150 { get; set; } = true;

    [JsonPropertyName("logo71")]
    public bool Logo71 { get; set; } = true;

    [JsonPropertyName("superhero")]
    public bool Superhero { get; set; } = false;
}

public class SubmissionOptionsConfig
{
    [JsonPropertyName("publishMode")]
    public string PublishMode { get; set; } = "Manual";

    [JsonPropertyName("runFullTrustReason")]
    public string RunFullTrustReason { get; set; } = "这是一个 WinUI 3 桌面应用，需要以全信任桌面进程运行才能正常启动并提供本地通知、文件和系统集成功能。应用仅在用户本机运行，不访问或修改其他用户的数据。";
}
