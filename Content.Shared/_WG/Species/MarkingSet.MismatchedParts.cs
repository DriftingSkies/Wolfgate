using Content.Shared._WG.Species;
using Robust.Shared.Prototypes;

namespace Content.Shared.Humanoid.Markings;

public sealed partial class MarkingSet
{
    /// <summary>
    /// Builds a set on the species' points, with one point for every category it has none for when the Mismatched parts
    /// option is on, and adds the markings in order.
    /// </summary>
    public static MarkingSet ForProfile(
        List<Marking> markings,
        string pointsPrototype,
        bool mismatchedParts,
        MarkingManager? markingManager = null,
        IPrototypeManager? prototypeManager = null)
    {
        if (!mismatchedParts)
            return new MarkingSet(markings, pointsPrototype, markingManager, prototypeManager);

        IoCManager.Resolve(ref markingManager);
        var set = new MarkingSet(pointsPrototype, markingManager, prototypeManager);
        set.OpenPoints(true);
        foreach (var marking in markings)
        {
            if (markingManager.TryGetMarking(marking, out var prototype))
                set.AddBack(prototype.MarkingCategory, marking);
        }

        return set;
    }

    /// <summary>With the Mismatched parts option on, gives one point to every category of this empty set that has none.</summary>
    public void OpenPoints(bool mismatchedParts)
    {
        if (mismatchedParts)
            MismatchedPartsRules.OpenPoints(Points);
    }

    /// <summary>
    /// <see cref="EnsureSpecies(string, Color?, MarkingManager?, IPrototypeManager?)"/>, except that with the Mismatched
    /// parts option on it keeps other species' markings: it only drops unknown ones and ones the species' sprite can't
    /// draw, and recolours skin-matching ones.
    /// </summary>
    public void EnsureSpecies(
        string species,
        Color? skinColor,
        bool mismatchedParts,
        MarkingManager? markingManager = null,
        IPrototypeManager? prototypeManager = null)
    {
        if (!mismatchedParts)
        {
            EnsureSpecies(species, skinColor, markingManager, prototypeManager);
            return;
        }

        IoCManager.Resolve(ref markingManager, ref prototypeManager);

        var toRemove = new List<(MarkingCategories category, string id)>();
        foreach (var (category, list) in Markings)
        {
            foreach (var marking in list)
            {
                if (!markingManager.TryGetMarking(marking, out var prototype)
                    || !MismatchedPartsRules.Drawable(prototype, species, prototypeManager))
                    toRemove.Add((category, marking.MarkingId));
            }
        }

        foreach (var (category, id) in toRemove)
            Remove(category, id);

        if (skinColor == null)
            return;

        foreach (var list in Markings.Values)
        {
            foreach (var marking in list)
            {
                if (markingManager.TryGetMarking(marking, out var prototype)
                    && markingManager.MustMatchSkin(species, prototype.BodyPart, out var alpha, prototypeManager))
                    marking.SetColor(skinColor.Value.WithAlpha(alpha));
            }
        }
    }
}
