using System.Linq;

namespace Content.Shared._WG.MappingMode;

/// <summary>Picks which library versions go when a map has more than it may keep.</summary>
public static class WGMappingPrune
{
    /// <summary>
    /// The versions to remove so at most <paramref name="max"/> stay: the oldest autosaves first, then the oldest
    /// manual saves. The newest version always stays.
    /// </summary>
    public static List<WGMappingVersionName> SelectForRemoval(IReadOnlyCollection<WGMappingVersionName> versions, int max)
    {
        var result = new List<WGMappingVersionName>();
        max = Math.Max(1, max);
        var excess = versions.Count - max;
        if (excess <= 0)
            return result;

        var newest = versions.Max(v => v.Number);
        var candidates = versions
            .Where(v => v.Number != newest)
            .OrderBy(v => v.Kind == WGMappingVersionKind.Auto ? 0 : 1)
            .ThenBy(v => v.Number);

        foreach (var version in candidates)
        {
            if (result.Count >= excess)
                break;

            result.Add(version);
        }

        return result;
    }
}
