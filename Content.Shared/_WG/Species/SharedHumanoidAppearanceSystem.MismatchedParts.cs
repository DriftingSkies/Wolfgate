using System.Linq;
using Content.Shared._WG.Species;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;

namespace Content.Shared.Humanoid;

public abstract partial class SharedHumanoidAppearanceSystem
{
    /// <summary>
    /// Copies the profile's Mismatched parts option onto the body and adds the hair and facial hair it unlocks. The
    /// normal hair path checks the species, so these are added after it, forced past the species' point budget.
    /// </summary>
    private void AddMismatchedHair(
        EntityUid uid,
        HumanoidCharacterProfile profile,
        HumanoidAppearanceComponent humanoid,
        Color hairColor,
        Color facialHairColor)
    {
        humanoid.MismatchedParts = profile.MismatchedParts;
        AddMismatched(uid, profile, humanoid, MarkingCategories.Hair, profile.Appearance.HairStyleId, hairColor);
        AddMismatched(uid, profile, humanoid, MarkingCategories.FacialHair, profile.Appearance.FacialHairStyleId, facialHairColor);
    }

    private void AddMismatched(
        EntityUid uid,
        HumanoidCharacterProfile profile,
        HumanoidAppearanceComponent humanoid,
        MarkingCategories category,
        string style,
        Color color)
    {
        if (!MismatchedPartsRules.Unlocks(category, style, profile.Species, profile.Sex, profile.MismatchedParts, _markingManager, _proto))
            return;

        if (humanoid.MarkingSet.TryGetCategory(category, out var worn) && worn.Any(m => m.MarkingId == style))
            return;

        AddMarking(uid, style, color, sync: false, forced: true, humanoid: humanoid);
    }
}
