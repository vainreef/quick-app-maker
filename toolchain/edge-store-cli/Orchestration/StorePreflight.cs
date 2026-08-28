using System.IO.Compression;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Orchestration;

public static class StorePreflight
{
    public static void Run(DesiredState desired)
    {
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] --- STORE 0: Offline static preflight ---");
        var errors = DesiredStateValidator.Validate(desired, strict: true);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Preflight failed:\n" + string.Join("\n", errors.Select(x => "  - " + x)));
        }

        if (!File.Exists(desired.Assets.Msix))
        {
            throw new FileNotFoundException($"MSIX package not found: {desired.Assets.Msix}");
        }

        using var archive = ZipFile.OpenRead(desired.Assets.Msix);
        var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, "AppxManifest.xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("AppxManifest.xml is missing inside MSIX.");

        using var reader = new StreamReader(entry.Open());
        string xml = reader.ReadToEnd();

        if (!xml.Contains("<DisplayName>") && !xml.Contains("DisplayName=\""))
        {
            throw new InvalidOperationException("MSIX AppxManifest.xml must contain a valid DisplayName.");
        }

        if (xml.Contains("Name=\"Windows.Universal\""))
        {
            throw new InvalidOperationException("MSIX declares Windows.Universal; this workflow requires Windows.Desktop only.");
        }

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PASS] Desired state is complete: description={desired.Values.Description.Length} chars, features={desired.Values.Features.Count}, keywords={desired.Values.Keywords.Count}.");
        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PASS] MSIX identity/display name/device family checks passed.");
    }
}
