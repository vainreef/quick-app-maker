using System.IO.Compression;

namespace Vainreef.EdgeStore.State;

public static class DesiredStateValidator
{
    public static IReadOnlyList<string> Validate(DesiredState desired, bool strict)
    {
        var errors = new List<string>();

        Required(desired.ProductId, "productId");
        Required(desired.ProductName, "productName");
        Required(desired.Values.Description, "values.description");
        Required(desired.Values.ShortDescription, "values.shortDescription");

        if (desired.Values.Keywords.Count > 7)
            errors.Add($"values.keywords has {desired.Values.Keywords.Count} entries; maximum is 7.");
        if (desired.Values.Keywords.Any(x => x.Length > 40))
            errors.Add("Each keyword must be 40 characters or fewer.");

        if (desired.Listing.Screenshot)
            RequiredFile(desired.Assets.Screenshot, "assets.screenshot (at least one desktop screenshot)");
        RequiredFile(desired.Assets.Msix, "assets.msix");

        if (string.Equals(desired.Properties.Privacy, "Yes", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(desired.Properties.PrivacyPolicyUrl) && string.IsNullOrWhiteSpace(desired.Properties.PrivacyPolicyText))
                errors.Add("When properties.privacy is 'Yes', either properties.privacyPolicyUrl or properties.privacyPolicyText must be provided.");
        }

        if (DeclaresRunFullTrust(desired.Assets.Msix))
            Required(desired.SubmissionOptions.RunFullTrustReason, "submissionOptions.runFullTrustReason");

        if (strict && desired.Values.Features.Count == 0)
            errors.Add("values.features is empty; listing import did not produce product features.");
        if (strict && desired.Values.Keywords.Count == 0)
            errors.Add("values.keywords is empty; listing import did not produce search terms.");

        CheckEnabled(desired.Listing.Poster, desired.Assets.Poster, "assets.poster");
        CheckEnabled(desired.Listing.Boxart, desired.Assets.Boxart, "assets.boxart");
        CheckEnabled(desired.Listing.Logo300, desired.Assets.Logo300, "assets.logo300");
        CheckEnabled(desired.Listing.Logo150, desired.Assets.Logo150, "assets.logo150");
        CheckEnabled(desired.Listing.Logo71, desired.Assets.Logo71, "assets.logo71");
        CheckEnabled(desired.Listing.Superhero, desired.Assets.Superhero, "assets.superhero");

        return errors;

        void Required(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add($"{field} is required.");
        }

        void RequiredFile(string? path, string field)
        {
            if (string.IsNullOrWhiteSpace(path)) errors.Add($"{field} is required.");
            else if (!File.Exists(path)) errors.Add($"{field} does not exist: {path}");
        }

        void CheckEnabled(bool enabled, string? path, string field)
        {
            if (enabled) RequiredFile(path, field);
        }
    }

    public static bool DeclaresRunFullTrust(string? msixPath)
    {
        if (string.IsNullOrWhiteSpace(msixPath) || !File.Exists(msixPath)) return false;
        try
        {
            using var archive = ZipFile.OpenRead(msixPath);
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, "AppxManifest.xml", StringComparison.OrdinalIgnoreCase));
            if (entry == null) return false;
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd().Contains("runFullTrust", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
