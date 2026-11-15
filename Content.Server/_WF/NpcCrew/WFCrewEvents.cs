using Content.Server._WF.NpcCrew.Components;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.NpcCrew;

/// <summary>
/// Broadcast when a ship's crew weigh a ship weapon hit from a vessel: whoever knows the vessel for a friend of the
/// ship, by alliance or by the way it has been fighting, sets <see cref="Friendly"/>, and its fire is let pass.
/// </summary>
[ByRefEvent]
public record struct WFCrewFriendlyFireQueryEvent(EntityUid Grid, EntityUid Attacker)
{
    public bool Friendly;
}

/// <summary>
/// Raised on a crewman's home grid before the crew's own rules decide whether someone aboard, or a ship docking, is
/// welcome: a system that commands the ship sets <see cref="Authorized"/> and, for someone it turns away, the
/// <see cref="Response"/> to use instead of the crewman's boarding rule. Unanswered, company and faction decide.
/// </summary>
[ByRefEvent]
public record struct WFCrewAuthorizeEvent(EntityUid Crew, EntityUid Other)
{
    public bool? Authorized;
    public WFCrewSecurityResponse? Response;
}

/// <summary>
/// Raised on an NPC medic's home grid before he takes a patient: a system that commands the ship sets
/// <see cref="Refused"/> for someone its crew should not treat. For a clone the body may be gone: <see cref="PatientUser"/>
/// says whose it was.
/// </summary>
[ByRefEvent]
public record struct WFCrewMayTreatEvent(EntityUid Medic, EntityUid Patient)
{
    public NetUserId? PatientUser;
    public bool Refused;
}

/// <summary>
/// Raised on a crewman's home grid when someone hurts, shoves or robs him: a system that commands the ship sets
/// <see cref="Forgiven"/> for someone the crew take it from, and the deed is neither remembered, answered nor reported.
/// </summary>
[ByRefEvent]
public record struct WFCrewForgiveEvent(EntityUid Crew, EntityUid Attacker)
{
    public bool Forgiven;
}

/// <summary>Broadcast when an NPC medic finishes a treatment.</summary>
[ByRefEvent]
public readonly record struct WFCrewTreatedEvent(EntityUid Medic, EntityUid Patient, WFCrewMedicAction Action);

/// <summary>Raised on the crewman and broadcast when a crewman goes critical or dies.</summary>
[ByRefEvent]
public readonly record struct WFCrewMemberDownEvent(
    EntityUid Mob,
    string Group,
    ProtoId<WFCrewRolePrototype>? Role,
    bool Dead);

/// <summary>Reports order changes, distinguishing automatic departure continuation from new commands.</summary>
[ByRefEvent]
public readonly record struct WFPilotOrdersChangedEvent(EntityUid Mob, WFPilotOrder Orders, bool Continuation = false);

/// <summary>
/// Raised on a crew pilot and broadcast when it reaches its last waypoint, or has backed off after undocking, and
/// switches to Hold.
/// </summary>
[ByRefEvent]
public readonly record struct WFPilotOrdersCompletedEvent(EntityUid Mob);

/// <summary>Raised on a crew pilot and broadcast when a Dock order ends docked; the pilot then holds.</summary>
[ByRefEvent]
public readonly record struct WFPilotDockedEvent(EntityUid Mob, EntityUid Grid, EntityUid TargetGrid);

/// <summary>Raised broadcast when a ship flown by the simulation, with no crew aboard, docks at a target for a Dock order.</summary>
[ByRefEvent]
public readonly record struct WFCrewVirtualDockedEvent(EntityUid Grid, EntityUid TargetGrid);

/// <summary>
/// Raised on a crew pilot and broadcast when a Dock order is given up and the pilot holds. The target may no longer
/// exist.
/// </summary>
[ByRefEvent]
public readonly record struct WFPilotDockFailedEvent(EntityUid Mob, EntityUid Grid, EntityUid TargetGrid);

/// <summary>Raised on a crew pilot and broadcast when it takes a helm.</summary>
[ByRefEvent]
public readonly record struct WFHelmTakenEvent(EntityUid Mob, EntityUid Console, EntityUid Grid);

/// <summary>Raised on a crew pilot and broadcast when it lets go of the helm, for whatever reason.</summary>
[ByRefEvent]
public readonly record struct WFHelmReleasedEvent(EntityUid Mob);

/// <summary>Broadcast when a ship's crew discovers new shared hostiles.</summary>
[ByRefEvent]
public readonly record struct WFCrewAlertEvent(EntityUid Grid, string Group, EntityUid[] Hostiles);

/// <summary>Broadcast once when a ship's shared crew alert ends.</summary>
[ByRefEvent]
public readonly record struct WFCrewAlertClearedEvent(EntityUid Grid, string Group);

/// <summary>Broadcast when two undocked grids collide, with each one's speed at the point and their closing speed.</summary>
[ByRefEvent]
public readonly record struct WFShipCollisionEvent(EntityUid Grid, EntityUid Other, float Speed, float OtherSpeed, float ClosingSpeed);

/// <summary>Broadcast when a ship weapon from another grid damages an anchored hull entity, or another grid rams it.</summary>
[ByRefEvent]
public readonly record struct WFCrewHullHitEvent(EntityUid Grid, EntityUid AttackerGrid);

/// <summary>
/// Raised on a crew's home grid when the crew turn on someone: an attacker, a thief, a trespasser past his warning.
/// <see cref="Sighting"/> marks hostility from the boarding rule alone, which a policy change can withdraw.
/// </summary>
[ByRefEvent]
public readonly record struct WFCrewAttackAnsweredEvent(string Group, EntityUid Attacker, bool Sighting = false);
