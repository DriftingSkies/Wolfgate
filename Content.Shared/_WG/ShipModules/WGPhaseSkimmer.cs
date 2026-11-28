using System.Numerics;
using Robust.Shared.Audio;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// Phase skimmer: each use phases the hull out and drops it back <see cref="Range"/> further along the way it is
/// travelling, short of anything in the way. Missiles chasing it lose their lock.
/// </summary>
public sealed partial class WGPhaseSkimmerEffect : WGShipModuleEffect
{
    /// <summary>How far a hull up to <see cref="ReferenceMass"/> skims, in metres.</summary>
    [DataField]
    public float Range = 160f;

    /// <summary>Hull mass up to which a skim goes its full range; a heavier hull skims less in proportion.</summary>
    [DataField]
    public float ReferenceMass = 120f;

    /// <summary>Smallest share of <see cref="Range"/> a heavy hull still skims.</summary>
    [DataField]
    public float MinScale = 0.3f;

    /// <summary>Shortest share of its range a skim may be cut to by something in the way; any shorter and it is refused.</summary>
    [DataField]
    public float MinShare = 0.25f;

    /// <summary>Speed above which the hull skims along its velocity rather than the held direction or its nose.</summary>
    [DataField]
    public float DriftSpeed = 2f;

    /// <summary>Share of the ship's heat capacity each skim adds, as soft heat.</summary>
    [DataField]
    public float Heat = 0.05f;

    /// <summary>Heard by those near the place the hull left.</summary>
    [DataField]
    public SoundSpecifier? DepartSound;

    /// <summary>Heard by those near the place the hull comes back.</summary>
    [DataField]
    public SoundSpecifier? ArriveSound;
}

/// <summary>A ship skimmed: clients draw its fading ghost where it left and a shimmer where it came back.</summary>
[Serializable, NetSerializable]
public sealed class WGPhaseSkimEvent : EntityEventArgs
{
    public NetEntity Ship;

    /// <summary>World position and rotation it left from.</summary>
    public Vector2 From;
    public Angle FromRotation;

    /// <summary>World position it arrived at.</summary>
    public Vector2 To;

    public WGPhaseSkimEvent(NetEntity ship, Vector2 from, Angle fromRotation, Vector2 to)
    {
        Ship = ship;
        From = from;
        FromRotation = fromRotation;
        To = to;
    }
}
