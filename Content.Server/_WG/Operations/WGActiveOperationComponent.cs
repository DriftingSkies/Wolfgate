using System.Numerics;
using Content.Shared._WG.Operations;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WG.Operations;

/// <summary>An operation in progress, on an entity of its own in nullspace.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WGActiveOperationComponent : Component
{
    [DataField]
    public ProtoId<WGOperationPrototype> Operation;

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

    /// <summary>The uplink that paid for the operation; its refund or reward goes back there.</summary>
    [DataField]
    public EntityUid? Store;

    /// <summary>The NPCs the site spawned; a PvE operation succeeds when all are down.</summary>
    [DataField]
    public List<EntityUid> Npcs = new();

    /// <summary>The guards the operation spawned itself rather than its map; every one must go down to clear it.</summary>
    [DataField]
    public HashSet<EntityUid> Brought = new();

    /// <summary>When the site arrived, for picking up guards its map spawns a little later.</summary>
    [DataField, AutoPausedField]
    public TimeSpan ArrivedAt;

    /// <summary>The tick the site began loading: only NPCs made since then are its guards, not wildlife already there.</summary>
    [ViewVariables]
    public GameTick ArrivedTick;

    /// <summary>Where spawners still on the site sat when it arrived; mobs that appear there later are its guards too.</summary>
    [ViewVariables]
    public List<Vector2> GuardSpawns = new();

    /// <summary>Guards seen going critical or dead; one deleted after that, such as gibbed, still counts as down.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Downed = new();

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
