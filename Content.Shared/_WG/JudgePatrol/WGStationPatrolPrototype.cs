using Content.Shared._WG.Encounters;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.JudgePatrol;

/// <summary>
/// A standing guard on one station: a number of ships, each its own encounter, that circle the station spaced evenly
/// round the orbit and are replaced a while after one is lost.
/// </summary>
[Prototype("wgStationPatrol")]
public sealed partial class WGStationPatrolPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The station guarded: the first whose grid name, station name or id contains one of these.</summary>
    [DataField(required: true)]
    public List<string> Stations = new();

    /// <summary>The encounter each ship flies as. Its first ship's first Circle order sets the orbit's radius.</summary>
    [DataField(required: true)]
    public ProtoId<WGEncounterPrototype> Encounter;

    /// <summary>How many ships keep the orbit.</summary>
    [DataField]
    public int Count = 2;

    /// <summary>Metres from the station's centre within which its ships go after hostile drones; zero leaves drones be.</summary>
    [DataField]
    public float DroneRange;

    /// <summary>Seconds before a lost ship is replaced.</summary>
    [DataField]
    public float Respawn = 1200f;
}
