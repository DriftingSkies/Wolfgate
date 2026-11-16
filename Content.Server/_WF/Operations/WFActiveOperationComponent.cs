using Content.Shared._WF.Operations;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WF.Operations;

/// <summary>An operation in progress, on an entity of its own in nullspace.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WFActiveOperationComponent : Component
{
    [DataField]
    public ProtoId<WFOperationPrototype> Operation;

    /// <summary>Where the site arrives.</summary>
    [DataField]
    public MapCoordinates Target;

    /// <summary>When the site arrives; null once it has.</summary>
    [DataField, AutoPausedField]
    public TimeSpan? ArrivesAt;

    /// <summary>When the site leaves.</summary>
    [DataField, AutoPausedField]
    public TimeSpan EndsAt;

    [DataField]
    public EntityUid? Grid;

    /// <summary>The NPCs the site spawned; a PvE operation succeeds when all are down.</summary>
    [DataField]
    public List<EntityUid> Npcs = new();

    /// <summary>When the site arrived, for picking up guards its map spawns a little later.</summary>
    [DataField, AutoPausedField]
    public TimeSpan ArrivedAt;

    /// <summary>Set once a cleared PvE site has paid the caller.</summary>
    [DataField]
    public bool Paid;

    /// <summary>The world's ground a planetside site claimed, and its index in that ground's sites.</summary>
    [DataField]
    public EntityUid? Ground;

    [DataField]
    public int SiteIndex = -1;

    /// <summary>The grid a planetside site loads, picked when its ground is claimed.</summary>
    [DataField]
    public ResPath? Path;
}
