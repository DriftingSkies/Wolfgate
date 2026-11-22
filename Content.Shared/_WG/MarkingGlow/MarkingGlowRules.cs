using System.Linq;
using Content.Shared.Humanoid.Markings;

namespace Content.Shared._WG.MarkingGlow;

/// <summary>
/// Which parts of a character glow: marking IDs, and keys for the eyes and hair. The keys start with '@', which a
/// stored marking ID can't hold, so they never clash with one.
/// </summary>
public static class MarkingGlowRules
{
    public const string Eyes = "@eyes";
    public const string Hair = "@hair";
    public const string FacialHair = "@facial_hair";

    /// <summary>More than any character wears; crafted data can't make the list any longer.</summary>
    public const int MaxEntries = 64;

    private const char Separator = ';';

    /// <summary>
    /// The entries worth keeping, sorted and without repeats: the keys, and the IDs of markings in
    /// <paramref name="worn"/>. A null worn list keeps every ID.
    /// </summary>
    public static List<string> Clean(IEnumerable<string>? glow, IEnumerable<Marking>? worn = null)
    {
        if (glow == null)
            return new List<string>();

        var ids = worn?.Select(marking => marking.MarkingId).ToHashSet();
        return glow
            .Where(entry => !string.IsNullOrEmpty(entry) && !entry.Contains(Separator))
            .Where(entry => IsKey(entry) || ids == null || ids.Contains(entry))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Take(MaxEntries)
            .ToList();
    }

    /// <summary>Whether an entry is one of the eye and hair keys rather than a marking ID.</summary>
    public static bool IsKey(string entry)
    {
        return entry is Eyes or Hair or FacialHair;
    }

    /// <summary>Whether a marking glows: by its ID, or as hair while the hair or facial hair key is on.</summary>
    public static bool Glows(IReadOnlyCollection<string> glow, MarkingPrototype marking)
    {
        if (glow.Count == 0)
            return false;

        return glow.Contains(marking.ID)
            || marking.MarkingCategory == MarkingCategories.Hair && glow.Contains(Hair)
            || marking.MarkingCategory == MarkingCategories.FacialHair && glow.Contains(FacialHair);
    }

    /// <summary>The list with an entry turned on or off.</summary>
    public static List<string> Set(IEnumerable<string> glow, string entry, bool on)
    {
        var set = glow.ToHashSet();
        if (on)
            set.Add(entry);
        else
            set.Remove(entry);

        return Clean(set);
    }

    /// <summary>The list as the database stores it.</summary>
    public static string ToStored(IEnumerable<string> glow)
    {
        return string.Join(Separator, Clean(glow));
    }

    public static List<string> FromStored(string? stored)
    {
        return string.IsNullOrEmpty(stored) ? new List<string>() : Clean(stored.Split(Separator));
    }
}
