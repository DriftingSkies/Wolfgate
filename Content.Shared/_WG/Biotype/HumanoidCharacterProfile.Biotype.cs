using Content.Shared._WG.Biotype;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    /// <summary>
    /// Organic or synthetic body. Default follows the species; validation turns it into the species' own.
    /// </summary>
    [DataField]
    public WGBiotype Biotype { get; set; }

    /// <summary>Copy of this profile with the given biotype.</summary>
    public HumanoidCharacterProfile WithBiotype(WGBiotype biotype)
    {
        return new(this) { Biotype = biotype };
    }
}
