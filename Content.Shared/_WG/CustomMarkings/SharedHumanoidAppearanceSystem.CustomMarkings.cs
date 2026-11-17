using Content.Shared._WG.CustomMarkings;
using Content.Shared.Preferences;

namespace Content.Shared.Humanoid;

public abstract partial class SharedHumanoidAppearanceSystem
{
    /// <summary>The custom markings a profile puts on a body: none while the feature is off.</summary>
    protected List<CustomMarking> ProfileCustomMarkings(HumanoidCharacterProfile profile)
    {
        return _cfgManager.GetCVar(CustomMarkingCVars.Enabled)
            ? CustomMarkingRules.Clean(profile.CustomMarkings, _cfgManager.GetCVar(CustomMarkingCVars.MaxWorn))
            : new List<CustomMarking>();
    }
}
