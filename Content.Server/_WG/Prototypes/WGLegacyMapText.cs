using System.Text;
using System.Text.RegularExpressions;

namespace Content.Server._WG.Prototypes;

/// <summary>Moves the text of a saved map off the old WF prefix: prototype ids, component names, type tags and paths.</summary>
/// <remarks>The same rules as Tools/_WG/Prototypes/wg_prefix_rename.py, which renamed the repository.</remarks>
public static class WGLegacyMapText
{
    private static readonly (Regex Rule, string To)[] Rules =
    {
        (new Regex("(?<![A-Za-z0-9])WF(?![a-z])", RegexOptions.Compiled), "WG"),
        (new Regex("(?<=[a-z])WF(?=[A-Z][a-z])", RegexOptions.Compiled), "WG"),
        (new Regex("(?<![A-Za-z0-9])IWF(?=[A-Z][a-z])", RegexOptions.Compiled), "IWG"),
        (new Regex("(?<=[A-Z])_WF(?=_)", RegexOptions.Compiled), "_WG"),
        (new Regex("(?<![A-Z0-9])Wf(?=[A-Z][a-z])", RegexOptions.Compiled), "Wg"),
        (new Regex("(?<![A-Za-z0-9])wf(?=[-._A-Z])", RegexOptions.Compiled), "wg"),
        (new Regex("(?<![A-Za-z0-9])wF(?=[A-Z][a-z])", RegexOptions.Compiled), "wG"),
        (new Regex("(?<![A-Za-z0-9])wf(?![A-Za-z0-9_])", RegexOptions.Compiled), "wg"),
        (new Regex("(?<![A-Za-z0-9])wf(?=(?:cavern|planet|lattice|hallucinate|crew|antenna|tabs)|[hgac](?![A-Za-z0-9]))", RegexOptions.Compiled), "wg"),
    };

    /// <summary>Whether the text may hold the old prefix at all.</summary>
    public static bool MayNeedConvert(string text)
    {
        return text.Contains("WF", StringComparison.Ordinal)
               || text.Contains("Wf", StringComparison.Ordinal)
               || text.Contains("wf", StringComparison.Ordinal);
    }

    /// <summary>The text under the current prefix; the same instance when nothing changes.</summary>
    public static string Convert(string text)
    {
        if (!MayNeedConvert(text))
            return text;

        var result = new StringBuilder(text.Length);
        var changed = false;
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            end = end < 0 ? text.Length : end + 1;
            var line = text[start..end];
            start = end;

            // A chunk's tiles are base64, where the prefix can turn up by chance.
            if (!line.AsSpan().TrimStart().StartsWith("tiles:") && MayNeedConvert(line))
            {
                var converted = line;
                foreach (var (rule, to) in Rules)
                    converted = rule.Replace(converted, to);

                changed |= !ReferenceEquals(converted, line) && converted != line;
                line = converted;
            }

            result.Append(line);
        }

        return changed ? result.ToString() : text;
    }
}
