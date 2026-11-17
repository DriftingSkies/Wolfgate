using Content.Shared._WG.NpcCrew;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.NpcVehicleCrew;

/// <summary>An NPC-crewed vehicle: what to spawn, who sits where, whose side they are on and how they drive and fight.</summary>
[Prototype("wgVehicleCrew")]
public sealed partial class WGVehicleCrewPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>What the admin panel calls this crew; left out, its id.</summary>
    [DataField]
    public LocId? Name;

    /// <summary>The vehicle spawned, an armed variant for a fighting crew.</summary>
    [DataField(required: true)]
    public EntProtoId Vehicle;

    /// <summary>Role of the crewman in the driver seat.</summary>
    [DataField(required: true)]
    public ProtoId<WGCrewRolePrototype> Driver;

    /// <summary>Role of the crewman in the primary weapons seat; null for an unarmed vehicle.</summary>
    [DataField]
    public ProtoId<WGCrewRolePrototype>? Gunner;

    /// <summary>Passengers in the other seats, or standing in the cabin; they dismount on a bail-out.</summary>
    [DataField]
    public List<ProtoId<WGCrewRolePrototype>> Troopers = new();

    /// <summary>Bodies, names and loadouts by role; left out, each role keeps its own mob and gear.</summary>
    [DataField]
    public ProtoId<WGCrewProfilePrototype>? Profile;

    [DataField]
    public ProtoId<NpcFactionPrototype> Faction = "WGCrew";

    /// <summary>Behaviour at spawn: Hold, Patrol, Hunt, Escort or Flee.</summary>
    [DataField]
    public WGVehicleBehaviour Behaviour = WGVehicleBehaviour.Patrol;

    /// <summary>OnSight fights faction hostiles; WhenAttacked only whoever attacks the vehicle or its crew.</summary>
    [DataField]
    public WGCrewEngagement Engagement = WGCrewEngagement.OnSight;

    /// <summary>Tiles; targets farther than this are ignored.</summary>
    [DataField]
    public float EngageRange = 16f;

    /// <summary>Tiles; the distance the vehicle prefers to fight from.</summary>
    [DataField]
    public float Standoff = 8f;

    /// <summary>Whether it shoots while driving; off, it stops to fight.</summary>
    [DataField]
    public bool FireOnTheMove = true;

    /// <summary>Tiles round home for generated patrol points.</summary>
    [DataField]
    public float PatrolRadius = 40f;

    /// <summary>How long a patrol waits at each point; 2 s or less makes it roll through them without stopping.</summary>
    [DataField]
    public TimeSpan PatrolPauseMin = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan PatrolPauseMax = TimeSpan.FromSeconds(20);

    /// <summary>Hold only: a stranger inside it is warned with the horn and then counts as hostile; 0 disables.</summary>
    [DataField]
    public float GuardRadius;

    /// <summary>Seconds a warned stranger has to leave the guard radius.</summary>
    [DataField]
    public float GuardWarnTime = 15f;

    /// <summary>Frame integrity fraction below which the vehicle retreats home.</summary>
    [DataField]
    public float RetreatIntegrity = 0.35f;

    /// <summary>Frame integrity fraction below which the crew abandons the vehicle.</summary>
    [DataField]
    public float BailIntegrity = 0.1f;

    /// <summary>The gunner's first-shot delay and aim error, and the crewmen's own skill.</summary>
    [DataField]
    public WGCrewSkill Skill = WGCrewSkill.Veteran;

    /// <summary>Whether a target sends it hunting; null is true for Patrol and false for everything else.</summary>
    [DataField]
    public bool? Pursues;
}
