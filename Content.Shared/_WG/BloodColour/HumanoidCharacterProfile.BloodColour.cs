using Content.Shared._WG.BloodColour;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>Blood and flesh colour picked in the creator; null keeps the species' own.</summary>
    [DataField]
    public Color? BloodColor { get; set; }

    /// <summary>Copy of this profile with the given blood colour.</summary>
    public HumanoidCharacterProfile WithBloodColor(Color? color)
    {
        return new(this) { BloodColor = WGBloodColourRules.Validate(color) };
    }
}
