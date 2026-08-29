using System.Text.RegularExpressions;

namespace Vainreef.EdgeStore.State;

public static class ListingMarkdownImporter
{
    public static void Import(DesiredState state, string markdownPath)
    {
        string text = File.ReadAllText(markdownPath);
        var sections = ParseSections(text);

        string shortDescription = Find(sections, "short description", ["简短摘要", "简短描述"], []);
        string description = Find(sections, "description", ["完整描述", "应用描述"], ["short", "简短"]);
        string features = Find(sections, "features", ["app features", "产品功能", "核心功能", "主要功能"], []);
        string keywords = Find(sections, "keywords", ["search terms", "搜索关键词", "搜索关键字"], []);

        if (!string.IsNullOrWhiteSpace(shortDescription)) state.Values.ShortDescription = Plain(shortDescription);
        if (!string.IsNullOrWhiteSpace(description)) state.Values.Description = Plain(description);

        var featureItems = ListItems(features);
        if (featureItems.Count > 0) state.Values.Features = featureItems;

        var keywordItems = ListItems(keywords);
        if (keywordItems.Count == 0 && !string.IsNullOrWhiteSpace(keywords))
            keywordItems = keywords.Split([';', '；', ',', '，', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (keywordItems.Count > 0) state.Values.Keywords = keywordItems;
    }

    private static Dictionary<string, string> ParseSections(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var matches = Regex.Matches(text, @"(?m)^##\s+(.+?)\s*$");
        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index + matches[i].Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            result[matches[i].Groups[1].Value.Trim()] = text[start..end].Trim();
        }
        return result;
    }

    private static string Find(Dictionary<string, string> sections, string needle, params string[] aliases)
        => Find(sections, needle, aliases, []);

    private static string Find(Dictionary<string, string> sections, string needle, string alias, string[] exclude)
        => Find(sections, needle, [alias], exclude);

    private static string Find(Dictionary<string, string> sections, string needle, string[] aliases, string[] exclude)
    {
        var needles = new[] { needle }.Concat(aliases).ToArray();
        foreach (var pair in sections)
        {
            string h = pair.Key.ToLowerInvariant();
            if (exclude.Any(x => h.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
            if (needles.Any(x => h.Contains(x, StringComparison.OrdinalIgnoreCase))) return pair.Value;
        }
        return string.Empty;
    }

    private static List<string> ListItems(string section) => Regex.Matches(section ?? "", @"(?m)^[-*]\s+(.+)$")
        .Select(m => Plain(m.Groups[1].Value)).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

    private static string Plain(string value) => Regex.Replace(value.Trim(), @"\*\*(.*?)\*\*", "$1");
}
