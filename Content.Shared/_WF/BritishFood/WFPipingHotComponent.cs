using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Utility;

namespace Content.Shared._WF.BritishFood;

/// <summary>Food fresh out of a hot vendor: it steams, and bites burn until it cools.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class WFPipingHotComponent : Component
{
    /// <summary>Shortest time it stays hot from when it spawns.</summary>
    [DataField]
    public TimeSpan MinDuration = TimeSpan.FromSeconds(10);

    /// <summary>Longest time it stays hot from when it spawns.</summary>
    [DataField]
    public TimeSpan MaxDuration = TimeSpan.FromSeconds(30);

    /// <summary>When it has cooled. Rolled at map init unless already set.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan CoolAt;

    /// <summary>How long a burned eater is put off the next bite.</summary>
    [DataField]
    public TimeSpan ScaldPause = TimeSpan.FromSeconds(3);

    /// <summary>Until when the last burned eater is put off.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan PausedUntil;

    /// <summary>The burn each bite gives the eater's head.</summary>
    [DataField]
    public DamageSpecifier Burn = new() { DamageDict = new() { ["Heat"] = 1 } };

    /// <summary>Eaters who take heat damage only at or above this many kelvin don't feel it.</summary>
    [DataField]
    public float HeatProofThreshold = 400f;

    /// <summary>The steam drawn over the food.</summary>
    [DataField]
    public SpriteSpecifier Steam = new SpriteSpecifier.Rsi(new ResPath("/Textures/_WF/BritishFood/steam.rsi"), "steam");
}
