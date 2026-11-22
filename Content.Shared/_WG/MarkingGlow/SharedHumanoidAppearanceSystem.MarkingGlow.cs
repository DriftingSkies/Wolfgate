using Content.Shared._WG.MarkingGlow;
using Content.Shared.Preferences;

namespace Content.Shared.Humanoid;

public abstract partial class SharedHumanoidAppearanceSystem
{
    /// <summary>Whether chosen markings, eyes and hair glow on this server.</summary>
    protected bool MarkingGlowEnabled => _cfgManager.GetCVar(MarkingGlowCVars.Enabled);

    /// <summary>What a profile makes glow on a body: nothing while the feature is off.</summary>
    protected List<string> ProfileMarkingGlow(HumanoidCharacterProfile profile)
    {
        return MarkingGlowEnabled
            ? MarkingGlowRules.Clean(profile.MarkingGlow)
            : new List<string>();
    }
}
