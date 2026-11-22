using Content.Shared._WG.MarkingGlow;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

/// <summary>The glow switches: one per applied marking on the Markings tab, and ones for the eyes and hair.</summary>
public sealed partial class HumanoidProfileEditor
{
    private bool MarkingGlowEnabled => _cfgManager.GetCVar(MarkingGlowCVars.Enabled);

    private void InitializeMarkingGlow()
    {
        Markings.IsGlowing = id => Profile?.Glows(id) == true;
        Markings.OnGlowChanged += SetMarkingGlow;
        // Runs after the profile takes the change, so glow goes with the marking taken off.
        Markings.OnMarkingRemoved += _ => PruneMarkingGlow();

        EyeGlowCheckBox.OnToggled += args => SetMarkingGlow(MarkingGlowRules.Eyes, args.Pressed);
        HairGlowCheckBox.OnToggled += args => SetMarkingGlow(MarkingGlowRules.Hair, args.Pressed);
        FacialHairGlowCheckBox.OnToggled += args => SetMarkingGlow(MarkingGlowRules.FacialHair, args.Pressed);

        _cfgManager.OnValueChanged(MarkingGlowCVars.Enabled, OnMarkingGlowEnabledChanged);
    }

    private void DisposeMarkingGlow()
    {
        _cfgManager.UnsubValueChanged(MarkingGlowCVars.Enabled, OnMarkingGlowEnabledChanged);
    }

    private void OnMarkingGlowEnabledChanged(bool _)
    {
        UpdateMarkingGlow();
    }

    private void UpdateMarkingGlow()
    {
        var enabled = MarkingGlowEnabled;
        Markings.IsGlowing = enabled ? id => Profile?.Glows(id) == true : null;

        EyeGlowCheckBox.Visible = enabled;
        Show(EyeGlowCheckBox, MarkingGlowRules.Eyes);
        UpdateHairGlow();
    }

    /// <summary>Shows the hair switches for the hair the species can have.</summary>
    private void UpdateHairGlow()
    {
        var enabled = MarkingGlowEnabled;
        HairGlowCheckBox.Visible = enabled && HairStylePicker.Visible;
        FacialHairGlowCheckBox.Visible = enabled && FacialHairPicker.Visible;
        HairGlowRow.Visible = HairGlowCheckBox.Visible || FacialHairGlowCheckBox.Visible;
        Show(HairGlowCheckBox, MarkingGlowRules.Hair);
        Show(FacialHairGlowCheckBox, MarkingGlowRules.FacialHair);
    }

    private void Show(BaseButton box, string entry)
    {
        box.Pressed = Profile?.Glows(entry) == true;
    }

    private void SetMarkingGlow(string entry, bool on)
    {
        if (Profile == null || Profile.Glows(entry) == on)
            return;

        Profile = Profile.WithMarkingGlow(entry, on);
        SetDirty();
        ReloadProfilePreview();
    }

    private void PruneMarkingGlow()
    {
        if (Profile == null)
            return;

        var kept = MarkingGlowRules.Clean(Profile.MarkingGlow, Profile.Appearance.Markings);
        if (kept.Count != Profile.MarkingGlow.Count)
            Profile.MarkingGlow = kept;
    }
}
