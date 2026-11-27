using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// Maneuvering jets: each use kicks the hull forward, back or sideways along the direction the pilot holds, adding
/// <see cref="DeltaV"/> over <see cref="BurnTime"/>.
/// </summary>
public sealed partial class WGManeuveringJetsEffect : WGShipModuleEffect
{
    /// <summary>Speed one burn adds to a hull of <see cref="ReferenceMass"/> or less, in metres per second.</summary>
    [DataField]
    public float DeltaV = 20f;

    /// <summary>Seconds a burn lasts.</summary>
    [DataField]
    public float BurnTime = 0.4f;

    /// <summary>
    /// Hull mass up to which a burn gives its full kick, about a 120-tile fighter; a heavier hull gets the same push,
    /// so its kick falls with the ratio.
    /// </summary>
    [DataField]
    public float ReferenceMass = 60f;

    /// <summary>Smallest share of <see cref="DeltaV"/> a heavy hull still gets.</summary>
    [DataField]
    public float MinScale = 0.1f;

    /// <summary>Heat one burn adds to the ship's thermal signature.</summary>
    [DataField]
    public float Heat = 1000000f;
}

/// <summary>A jets burn started: the ship's thrusters that push along it flare and puff smoke rings.</summary>
[Serializable, NetSerializable]
public sealed class WGManeuveringJetsPuffEvent : EntityEventArgs
{
    /// <summary>The thrusters that puff, with their share of the burn.</summary>
    public List<WGJetsPuff> Puffs = new();

    /// <summary>Seconds the burn lasts.</summary>
    public float BurnTime;
}

/// <summary>One thruster's share of a burn: its size against a standard thruster and how squarely it pushes along it.</summary>
[Serializable, NetSerializable]
public record struct WGJetsPuff(NetEntity Thruster, float Size, float Strength);
