using Content.Shared._Mono.Company;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Operations;

/// <summary>
/// An operation a faction calls in from its uplink: a site that spawns away from both faction bases. A PvP site holds
/// payloads that pay whichever faction brings them home; a PvE site holds NPCs that pay the caller once cleared.
/// </summary>
[Prototype("wgOperation")]
public sealed partial class WGOperationPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<WGOperationPrototype>))]
    public string[]? Parents { get; private set; }

    /// <summary>Never inherited, or every operation built on an abstract base would be abstract too.</summary>
    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; private set; }

    /// <summary>The site's name, shown on radar and in announcements.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>The faction that calls it in.</summary>
    [DataField(required: true)]
    public ProtoId<CompanyPrototype> Company;

    /// <summary>The jobs that may call it; empty for any member of the faction.</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();

    /// <summary>The grids the site picks from.</summary>
    [DataField(required: true)]
    public List<ResPath> Grids = new();

    /// <summary>Distance range from the sector's centre.</summary>
    [DataField]
    public float MinDistance = 4000f;

    [DataField]
    public float MaxDistance = 9000f;

    /// <summary>The site keeps at least this far from either faction base.</summary>
    [DataField]
    public float BaseClearance = 3000f;

    /// <summary>Warning before the site arrives.</summary>
    [DataField]
    public TimeSpan Inbound = TimeSpan.FromMinutes(3);

    /// <summary>How long the site stays once it arrives.</summary>
    [DataField]
    public TimeSpan Duration = TimeSpan.FromMinutes(25);

    /// <summary>How long after it ends the faction can call it again.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(45);

    /// <summary>What the call cost, in the caller's faction currency; failed PvE operations refund part of it.</summary>
    [DataField]
    public int Stake;

    /// <summary>Share of the stake a failed PvE operation refunds.</summary>
    [DataField]
    public float FailRefund = 0.5f;

    /// <summary>Paid to the caller, on top of the stake, when every NPC is down; an operation with a reward is PvE.</summary>
    [DataField]
    public int Reward;

    /// <summary>Entities spawned on the site that pay whoever turns them in.</summary>
    [DataField]
    public List<WGOperationSpawn> Payloads = new();

    /// <summary>Entities mapped on the grid swapped for others as it arrives, such as a vault's server for a payload.</summary>
    [DataField]
    public Dictionary<EntProtoId, EntProtoId> Replace = new();

    /// <summary>Spoils spawned on a PvE site, at its warp point or an open tile, when it is cleared.</summary>
    [DataField]
    public List<WGOperationSpawn> ClearSpawns = new();

    /// <summary>NPCs that guard the site.</summary>
    [DataField]
    public List<WGOperationSpawn> Npcs = new();

    /// <summary>One more NPC, of the first kind, for this many caller faction members online; 0 for none.</summary>
    [DataField]
    public int NpcPerMembers;

    /// <summary>
    /// The NPC faction every guard and turret that comes with a PvE site joins, so a map's own guards can't side with
    /// the caller; null leaves them in their mapped factions.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype>? GuardFaction;

    /// <summary>Components added to the site's grid.</summary>
    [DataField]
    public ComponentRegistry AddComponents = new();

    /// <summary>Who hears where the site is: only the caller's faction, or the whole sector.</summary>
    [DataField]
    public WGOperationBroadcast Broadcast = WGOperationBroadcast.Sector;

    /// <summary>Announced coordinates are rounded to this many metres.</summary>
    [DataField]
    public int Precision = 500;

    /// <summary>Chance the site lands on a world's ground instead of in space; 1 for a planetside operation.</summary>
    [DataField]
    public float PlanetChance;

    /// <summary>The grids a planetside site picks from; empty to use <see cref="Grids"/>.</summary>
    [DataField]
    public List<ResPath> PlanetGrids = new();

    /// <summary>Added to the warning and the stay of a planetside site, for the hop down and the landing.</summary>
    [DataField]
    public TimeSpan PlanetExtraTime = TimeSpan.FromMinutes(5);

    /// <summary>Announced coordinates on a world are rounded to this many metres.</summary>
    [DataField]
    public int PlanetPrecision = 50;

    /// <summary>World surfaces a planetside site never lands on.</summary>
    [DataField]
    public List<string> ExcludedWorlds = new() { "WGSurfaceCarcinoma" };
}

/// <summary>Who an operation's announcements reach.</summary>
public enum WGOperationBroadcast : byte
{
    Faction,
    Sector,
}

/// <summary>A number of one entity.</summary>
[DataDefinition]
public partial record struct WGOperationSpawn
{
    [DataField(required: true)]
    public EntProtoId Proto;

    [DataField]
    public int Count = 1;

    /// <summary>Spread over open tiles rather than placed at the site's warp point.</summary>
    [DataField]
    public bool Scatter;
}

/// <summary>Raised on the buyer when an operation's uplink listing is bought. Sent to clients with the listing.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class WGCallOperationEvent : EntityEventArgs
{
    [DataField(required: true)]
    public ProtoId<WGOperationPrototype> Operation;
}
