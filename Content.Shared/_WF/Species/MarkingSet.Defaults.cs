using System.Linq;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Humanoid.Markings;

public sealed partial class MarkingSet
{
    /// <summary>
    /// The markings with the species' default markings added to every category that has none, so a new look starts
    /// with them saved in the profile. Optional defaults are not re-added on load, so the player can take them off.
    /// </summary>
    public static List<Marking> WithSpeciesDefaults(
        List<Marking> markings,
        string species,
        bool mismatchedParts,
        Color skinColor,
        Color eyeColor,
        MarkingManager? markingManager = null,
        IPrototypeManager? prototypeManager = null)
    {
        IoCManager.Resolve(ref markingManager, ref prototypeManager);
        if (!prototypeManager.TryIndex<SpeciesPrototype>(species, out var speciesProto))
            return markings;

        var set = ForProfile(markings, speciesProto.MarkingPoints, mismatchedParts, markingManager, prototypeManager);
        set.EnsureSpecies(species, skinColor, mismatchedParts, markingManager, prototypeManager);

        foreach (var (category, points) in set.Points)
        {
            if (category == MarkingCategories.Chassis || points.DefaultMarkings.Count == 0 ||
                set.Markings.TryGetValue(category, out var applied) && applied.Count > 0)
                continue;

            foreach (var id in points.DefaultMarkings)
            {
                if (points.Points <= 0 || !markingManager.Markings.TryGetValue(id, out var proto))
                    continue;

                var colors = MarkingColoring.GetMarkingLayerColors(proto, skinColor, eyeColor, set);
                set.AddBack(category, new Marking(id, colors));
            }
        }

        return set.GetForwardEnumerator().ToList();
    }
}
