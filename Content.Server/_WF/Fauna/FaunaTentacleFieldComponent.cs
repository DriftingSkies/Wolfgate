using Content.Shared.Maps;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// While the mob is awake, tentacles keep bursting out of the open ground around it.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaTentacleFieldComponent : Component
{
    /// <summary>
    /// Spawned on each tile that gets a tentacle.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Tentacle;

    /// <summary>
    /// Tiles around the mob that tentacles can rise in.
    /// </summary>
    [DataField]
    public int Radius = 4;

    /// <summary>
    /// Chance each open tile in range gets a tentacle per interval.
    /// </summary>
    [DataField]
    public float Chance = 0.05f;

    /// <summary>
    /// Time between tentacle rolls.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Floors the tentacles can rise from, such as asteroid rock. Empty allows any floor.
    /// </summary>
    [DataField]
    public List<ProtoId<ContentTileDefinition>> Tiles = new();

    /// <summary>
    /// When the next tentacle roll happens.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextSpawn;
}
