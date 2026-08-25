using System.IO.Compression;
using Vainreef.EdgeStore.State;

var root = Path.Combine(Path.GetTempPath(), "edge-store-cli-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string md = Path.Combine(root, "listing.md");
    File.WriteAllText(md, """
    ## 简短摘要（Short Description）
    简短文案
    ## 完整描述（Description）
    完整文案
    ## 产品功能（App Features）
    - 功能一
    - 功能二
    ## 搜索关键词（Search Terms）
    甲;乙;丙
    """);
    var state = new DesiredState();
    ListingMarkdownImporter.Import(state, md);
    Equal("简短文案", state.Values.ShortDescription, "Chinese short description heading");
    Equal("完整文案", state.Values.Description, "Chinese description heading");
    Equal(2, state.Values.Features.Count, "Chinese features heading");
    Equal(3, state.Values.Keywords.Count, "semicolon keywords");

    string screenshot = Path.Combine(root, "shot.png");
    File.WriteAllBytes(screenshot, [1]);
    string msix = Path.Combine(root, "test.msix");
    using (var zip = ZipFile.Open(msix, ZipArchiveMode.Create))
    {
        var entry = zip.CreateEntry("AppxManifest.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("<Package><Application EntryPoint=\"Windows.FullTrustApplication\"><Extensions><desktop6:Extension Category=\"windows.fullTrustProcess\"/></Extensions></Application><Capability Name=\"runFullTrust\"/></Package>");
    }
    state.ProductId = "TEST";
    state.ProductName = "Test";
    state.Assets.Msix = msix;
    state.Assets.Screenshot = screenshot;
    state.Properties.Privacy = "No";
    state.SubmissionOptions.RunFullTrustReason = "";
    var errors = DesiredStateValidator.Validate(state, strict: true);
    True(errors.Any(x => x.Contains("runFullTrustReason")), "runFullTrust conditional requirement");
    True(errors.All(x => !x.Contains("privacyPolicyUrl")), "privacy=No does not require privacy URL/text");

    var cp = new StoreCheckpoint();
    cp.Mark("listing", PhaseStatus.AppliedUnverified, "clicked save");
    True(!cp.IsConverged("listing"), "applied is not converged");
    bool rejectedEvidenceFree = false;
    try { cp.MarkConverged("listing", "", ""); } catch (ArgumentException) { rejectedEvidenceFree = true; }
    True(rejectedEvidenceFree, "convergence requires overview evidence");
    cp.MarkConverged("listing", "Overview module=Complete", "https://example.test/overview");
    True(cp.IsConverged("listing"), "verified convergence is explicit");

    Console.WriteLine("PASS: edge-store-cli model tests");
    return 0;
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

static void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"FAIL {name}: expected={expected}, actual={actual}");
}

static void True(bool value, string name)
{
    if (!value) throw new Exception("FAIL " + name);
}
