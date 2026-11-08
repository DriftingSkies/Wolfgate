using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Warheads;

/// <summary>
/// A named warhead payload preset.
/// </summary>
[Prototype("wfWarheadPayload")]
public sealed partial class WFWarheadPayloadPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public WFWarheadPayload Payload = new();
}

/// <summary>
/// Everything a warhead does when it goes off: blast, radiation, fallout, EMP and the client effects.
/// </summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class WFWarheadPayload
{
    /// <summary>Explosion type; null leaves the blast to something else, such as the station nuke.</summary>
    [DataField]
    public string? ExplosionType;

    [DataField]
    public float TotalIntensity;

    [DataField]
    public float IntensitySlope = 5f;

    [DataField]
    public float MaxIntensity = 100f;

    [DataField]
    public float TileBreakScale = 1f;

    /// <summary>Deletes the grid it sits on outright, then blasts the space around it. Never deletes a planet.</summary>
    [DataField]
    public bool DeleteGrid;

    /// <summary>Metres around ground zero where everything is deleted, floor and decals too, before the blast runs.</summary>
    [DataField]
    public float EraseRadius;

    /// <summary>Radiation burst at detonation, in rads per second at the centre.</summary>
    [DataField]
    public float PromptRadiation;

    [DataField]
    public float PromptRadiationSlope = 0.05f;

    /// <summary>Seconds the burst lasts.</summary>
    [DataField]
    public float PromptRadiationDuration = 5f;

    [DataField]
    public string FalloutPrototype = "WFNuclearFallout";

    /// <summary>Fallout sources scattered around ground zero, besides the one at the centre.</summary>
    [DataField]
    public int FalloutCount;

    /// <summary>Tiles the scattered fallout lands within.</summary>
    [DataField]
    public float FalloutSpread = 10f;

    [DataField]
    public float FalloutIntensity;

    [DataField]
    public float FalloutHalfLife = 120f;

    /// <summary>Haze radius of each fallout source, in tiles.</summary>
    [DataField]
    public float FalloutRadius = 6f;

    [DataField]
    public float EmpRange;

    [DataField]
    public float EmpEnergy = 50000f;

    [DataField]
    public float EmpDuration = 60f;

    /// <summary>Tiles within which the flash whites out the screen.</summary>
    [DataField]
    public float FlashRange = 40f;

    /// <summary>Largest radius of the fireball, in tiles.</summary>
    [DataField]
    public float FireballRadius = 4f;

    /// <summary>Tiles the shockwave travels before fading.</summary>
    [DataField]
    public float ShockwaveRange = 60f;

    /// <summary>Tiles per second the shockwave travels.</summary>
    [DataField]
    public float ShockwaveSpeed = 40f;

    /// <summary>Camera kick when the shockwave passes.</summary>
    [DataField]
    public float ShakeStrength = 2f;

    /// <summary>Seconds the fireball, or the mushroom cloud on a planet, lasts.</summary>
    [DataField]
    public float EffectDuration = 20f;

    /// <summary>Plays the countdown voice and alarm to the whole map rather than nearby players.</summary>
    [DataField]
    public bool MapWideAudio = true;

    [DataField]
    public string? DetonationSound = "/Audio/Effects/explosionfar.ogg";

    public WFWarheadPayload Clone()
    {
        return (WFWarheadPayload) MemberwiseClone();
    }
}
