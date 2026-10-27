using Content.Shared._WF.Genitals.Profile;
using Content.Shared.Preferences;
using Robust.Shared.Utility;

namespace Content.Server._WF.Genitals;

public sealed partial class GenitalOrganSystem
{
    /// <summary>
    /// Copies the profile's adult description onto the body. Like the anatomy, later loads that carry none (DNA
    /// scrambler, random re-rolls) keep the character's own; a body that is no longer eligible loses it.
    /// </summary>
    private void SetNsfwText(EntityUid uid, HumanoidCharacterProfile profile)
    {
        if (!_genitals.IsEligible(profile.Age, profile.Species.Id))
        {
            RemComp<NsfwDescriptionComponent>(uid);
            return;
        }

        // Crafted network data can deliver no anatomy at all. Markup is stripped: the card shows it as plain text.
        var text = FormattedMessage.RemoveMarkupPermissive(profile.Genitals?.NsfwText ?? string.Empty).Trim();
        if (text.Length == 0)
            return;

        EnsureComp<NsfwDescriptionComponent>(uid).Text =
            text.Length > GenitalProfile.MaxNsfwTextLength ? text[..GenitalProfile.MaxNsfwTextLength] : text;
    }
}

/// <summary>A body's adult description from its character profile. Server only; the examine card sends it to examiners who may read anatomy.</summary>
[RegisterComponent]
public sealed partial class NsfwDescriptionComponent : Component
{
    [DataField]
    public string Text = string.Empty;
}
