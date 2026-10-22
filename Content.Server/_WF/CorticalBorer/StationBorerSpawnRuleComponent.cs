using Content.Shared.Destructible.Thresholds;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.CorticalBorer;

/// <summary>
/// Spawns borers from the vents of one random station, never a ship or the sector's central station.
/// </summary>
[RegisterComponent, Access(typeof(StationBorerSpawnRule))]
public sealed partial class StationBorerSpawnRuleComponent : Component
{
    /// <summary>The entity spawned at each picked vent.</summary>
    [DataField]
    public EntProtoId Spawn = "MobCorticalBorer";

    /// <summary>How many vents get one.</summary>
    [DataField]
    public MinMax Count = new(1, 3);
}
