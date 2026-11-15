using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// A commanded ship's roster: one slot per crew post and counter post on the grid, staffed when the ship spawns and
/// restaffed by hand, or automatically once the owner switches that on, after a delay and within an hourly cap.
/// Crew share one NpcCrew group and the mission below; counters are station traders. A slot can be stood down (left
/// empty) or held by a player who took its job. Server only; slots are round state.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipRosterComponent : Component
{
    /// <summary>The NpcCrew group the roster's crew belong to.</summary>
    [DataField]
    public string Group = "command";

    /// <summary>The radio officer's callsign; empty for none.</summary>
    [DataField]
    public string Callsign = string.Empty;

    /// <summary>Where the marines stand when nothing is happening.</summary>
    [DataField]
    public WFShipMarineOrders MarineOrders = WFShipMarineOrders.Patrol;

    /// <summary>When the gunners open fire without being given a target.</summary>
    [DataField]
    public WFGunnerPolicy GunnerPolicy = WFGunnerPolicy.Hostiles;

    /// <summary>What the pilot does when another ship attacks.</summary>
    [DataField]
    public WFShipPilotStance PilotStance = WFShipPilotStance.Evade;

    /// <summary>The crew's own boarding rule, where ShipCommand's zones don't answer for a visitor.</summary>
    [DataField]
    public WFCrewSecurityResponse BoardingResponse = WFCrewSecurityResponse.Warn;

    /// <summary>The crew's answer to a ship docking unwelcome; ShipCommand welcomes docks by default.</summary>
    [DataField]
    public WFCrewSecurityResponse DockingResponse = WFCrewSecurityResponse.Ignore;

    /// <summary>How well the ship's crew shoot, lay guns and fly, and how much punishment they take.</summary>
    [DataField]
    public WFCrewSkill Skill = WFCrewSkill.Veteran;

    /// <summary>The crew are ordered to repair the ship on their own whenever they have nothing else queued.</summary>
    [DataField]
    public bool AutoRepair;

    /// <summary>How often an idle crew is set to repairs.</summary>
    [DataField]
    public TimeSpan RepairInterval = TimeSpan.FromSeconds(60);

    [ViewVariables]
    public TimeSpan NextRepairCheck;

    /// <summary>The crew's guns refill themselves when they run dry.</summary>
    [DataField]
    public bool InfiniteAmmo;

    /// <summary>Every post is staffed when the ship spawns.</summary>
    [DataField]
    public bool StaffOnSpawn = true;

    /// <summary>Lost crew are replaced on their own; off until the owner turns it on.</summary>
    [DataField]
    public bool AutoRestaff;

    /// <summary>How long a post stays empty before automatic restaffing fills it.</summary>
    [DataField]
    public TimeSpan RestaffDelay = TimeSpan.FromMinutes(2);

    /// <summary>The most automatic restaffs in any hour, so a fight can't turn the ship into a crew spawner.</summary>
    [DataField]
    public int RestaffsPerHour = 6;

    /// <summary>How long a relieved crewman has to reach the crew bay before he is taken off anyway.</summary>
    [DataField]
    public TimeSpan ReliefTimeout = TimeSpan.FromSeconds(60);

    [ViewVariables]
    public Dictionary<string, WFShipRosterSlot> Slots = new();

    /// <summary>Where crew come aboard and leave: the crew bay marker, if the ship has one.</summary>
    [ViewVariables]
    public EntityCoordinates? Bay;

    /// <summary>When the automatic restaffs of the last hour happened.</summary>
    [ViewVariables]
    public List<TimeSpan> Restaffs = new();
}

/// <summary>What stands at a roster post.</summary>
public enum WFShipRosterKind : byte
{
    /// <summary>An NpcCrew crewman of a role.</summary>
    Crew,

    /// <summary>A station trader behind a counter.</summary>
    Counter,
}

/// <summary>One post: what stands there and where, and who holds it.</summary>
public sealed class WFShipRosterSlot
{
    /// <summary>The slot's id on the roster: the post marker's, or one made from its role or trader.</summary>
    public string Id = string.Empty;

    /// <summary>A crew post or a counter.</summary>
    public WFShipRosterKind Kind;

    /// <summary>The crew role; unset for a counter.</summary>
    public ProtoId<WFCrewRolePrototype> Role;

    /// <summary>The trader at a counter; null for crew.</summary>
    public EntProtoId? Trader;

    /// <summary>The crewman's gear, when the post names one.</summary>
    public ProtoId<StartingGearPrototype>? Loadout;

    /// <summary>Where the post marker stands.</summary>
    public EntityCoordinates Post;

    /// <summary>Which way the post faces; a trader faces his counter.</summary>
    public Angle Rotation;

    /// <summary>The crewman or trader holding the post, alive or not.</summary>
    public EntityUid? Occupant;

    /// <summary>When the post was lost (its occupant died or was deleted); null while held.</summary>
    public TimeSpan? Down;

    /// <summary>Stood down from the bridge: nobody is sent to it until it is stood up again.</summary>
    public bool StoodDown;

    /// <summary>The player who took this post's job, while they hold it; no NPC is sent to it meanwhile.</summary>
    public EntityUid? Holder;

    /// <summary>The crewman stood down from this post, while he is on his way off the ship; nobody is sent until he has gone.</summary>
    public EntityUid? Leaving;

    /// <summary>What the slot's role or trader is called, for lists.</summary>
    public string What => Kind == WFShipRosterKind.Counter ? Trader?.Id ?? string.Empty : Role.Id;
}

/// <summary>Marks a crewman or trader as a roster member, so his death or removal frees his post.</summary>
[RegisterComponent]
public sealed partial class WFShipRosterMemberComponent : Component
{
    [ViewVariables]
    public EntityUid Grid;

    [ViewVariables]
    public string Slot = string.Empty;
}

/// <summary>A crewman relieved of his post, on his way to the crew bay, where he leaves the ship.</summary>
[RegisterComponent]
public sealed partial class WFShipRelievedComponent : Component
{
    [ViewVariables]
    public EntityCoordinates? Bay;

    /// <summary>When he is taken off wherever he is.</summary>
    [ViewVariables]
    public TimeSpan Deadline;
}

/// <summary>
/// A post on a commanded ship's roster. Unlike NpcCrew's spawn points nothing spawns here on its own: the ship's
/// roster staffs it. Saved with the map.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipCrewPostComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WFCrewRolePrototype> Role;

    [DataField]
    public ProtoId<StartingGearPrototype>? Loadout;

    /// <summary>The slot's id on the roster; empty for one made from the role and the marker.</summary>
    [DataField]
    public string Slot = string.Empty;
}

/// <summary>
/// A counter on a commanded ship's roster: the trader named here stands on the marker, facing the way the marker
/// faces, which should be the counter table. The roster staffs it; nothing spawns here on its own.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipCounterPostComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Trader;

    /// <summary>The slot's id on the roster; empty for one made from the trader and the marker.</summary>
    [DataField]
    public string Slot = string.Empty;
}

/// <summary>The crew bay of a commanded ship: restaffed crew come aboard here, and relieved crew leave from here.</summary>
[RegisterComponent]
public sealed partial class WFShipCrewBayComponent : Component;
