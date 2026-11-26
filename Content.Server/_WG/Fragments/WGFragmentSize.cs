using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WG.Fragments;

/// <summary>
/// A size of fragment: its own grids, and whatever it changes from the event's defaults. A greater fragment brings more
/// Remade in heavier waves, a tougher heart, its own announcements, and pays more. Anything left unset keeps the
/// default.
/// </summary>
[DataDefinition]
public sealed partial class WGFragmentSize
{
    [DataField(required: true)]
    public List<ResPath> Grids = new();

    /// <summary>The fragment's name on scanners.</summary>
    [DataField]
    public LocId? GridName;

    [DataField]
    public EntProtoId? Heart;

    [DataField]
    public int? Garrison;

    [DataField]
    public int? GarrisonElites;

    [DataField]
    public int? WaveBase;

    [DataField]
    public int? PlayersPerExtra;

    [DataField]
    public TimeSpan? WaveInterval;

    [DataField]
    public int? EliteEvery;

    [DataField]
    public int? MaxAlive;

    [DataField]
    public int? DecorCount;

    /// <summary>More scattered over it besides the decor: crystals, the dead.</summary>
    [DataField]
    public List<EntProtoId> Scatter = new();

    [DataField]
    public int ScatterCount;

    /// <summary>Multiplies each faction's reward.</summary>
    [DataField]
    public float RewardScale = 1f;

    [DataField]
    public int? ParticipantReward;

    [DataField]
    public WGFragmentLine? Sighted;

    [DataField]
    public WGFragmentLine? Arrived;

    [DataField]
    public WGFragmentLine? Warning;

    [DataField]
    public WGFragmentLine? Broken;

    [DataField]
    public WGFragmentLine? Lost;

    [DataField]
    public WGFragmentLine? Departed;
}
