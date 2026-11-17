using Content.Server._WG.NpcCrew.Components;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew;

/// <summary>
/// Broadcast when a ship's crew weigh a ship weapon hit from a vessel: whoever knows the vessel for a friend of the
/// ship, by alliance or by the way it has been fighting, sets <see cref="Friendly"/>, and its fire is let pass.
/// </summary>
[ByRefEvent]
public record struct WGCrewFriendlyFireQueryEvent(EntityUid Grid, EntityUid Attacker)
{
    public bool Friendly;
}

/// <summary>
/// Raised on a crewman's home grid before the crew's own rules decide whether someone aboard, or a ship docking, is
/// welcome: a system that commands the ship sets <see cref="Authorized"/> and, for someone it turns away, the
/// <see cref="Response"/> to use instead of the crewman's boarding rule. Unanswered, company and faction decide.
/// </summary>
[ByRefEvent]
public record struct WGCrewAuthorizeEvent(EntityUid Crew, EntityUid Other)
{
    public bool? Authorized;
    public WGCrewSecurityResponse? Response;
}

/// <summary>
/// Raised on an NPC medic's home grid before he takes a patient: a system that commands the ship sets
/// <see cref="Refused"/> for someone its crew should not treat. For a clone the body may be gone: <see cref="PatientUser"/>
/// says whose it was.
/// </summary>
[ByRefEvent]
public record struct WGCrewMayTreatEvent(EntityUid Medic, EntityUid Patient)
{
    public NetUserId? PatientUser;
    public bool Refused;
}

/// <summary>
/// Raised on a crewman's home grid when someone hurts, shoves or robs him: a system that commands the ship sets
/// <see cref="Forgiven"/> for someone the crew take it from, and the deed is neither remembered, answered nor reported.
/// </summary>
[ByRefEvent]
public record struct WGCrewForgiveEvent(EntityUid Crew, EntityUid Attacker)
{
    public bool Forgiven;
}

/// <summary>Broadcast when an NPC medic finishes a treatment.</summary>
[ByRefEvent]
public readonly record struct WGCrewTreatedEvent(EntityUid Medic, EntityUid Patient, WGCrewMedicAction Action);

/// <summary>Raised on the crewman and broadcast when a crewman goes critical or dies.</summary>
[ByRefEvent]
public readonly record struct WGCrewMemberDownEvent(
    EntityUid Mob,
    string Group,
    ProtoId<WGCrewRolePrototype>? Role,
    bool Dead);

/// <summary>Reports order changes, distinguishing automatic departure continuation from new commands.</summary>
[ByRefEvent]
public readonly record struct WGPilotOrdersChangedEvent(EntityUid Mob, WGPilotOrder Orders, bool Continuation = false);

/// <summary>
/// Raised on a crew pilot and broadcast when it reaches its last waypoint, or has backed off after undocking, and
/// switches to Hold.
/// </summary>
[ByRefEvent]
public readonly record struct WGPilotOrdersCompletedEvent(EntityUid Mob);

/// <summary>Raised on a crew pilot and broadcast when a Dock order ends docked; the pilot then holds.</summary>
[ByRefEvent]
public readonly record struct WGPilotDockedEvent(EntityUid Mob, EntityUid Grid, EntityUid TargetGrid);

/// <summary>Raised broadcast when a ship flown by the simulation, with no crew aboard, docks at a target for a Dock order.</summary>
[ByRefEvent]
public readonly record struct WGCrewVirtualDockedEvent(EntityUid Grid, EntityUid TargetGrid);

/// <summary>
/// Raised on a crew pilot and broadcast when a Dock order is given up and the pilot holds. The target may no longer
/// exist.
/// </summary>
[ByRefEvent]
public readonly record struct WGPilotDockFailedEvent(EntityUid Mob, EntityUid Grid, EntityUid TargetGrid);

/// <summary>Raised on a crew pilot and broadcast when it takes a helm.</summary>
[ByRefEvent]
public readonly record struct WGHelmTakenEvent(EntityUid Mob, EntityUid Console, EntityUid Grid);

/// <summary>Raised on a crew pilot and broadcast when it lets go of the helm, for whatever reason.</summary>
[ByRefEvent]
public readonly record struct WGHelmReleasedEvent(EntityUid Mob);

/// <summary>Broadcast when a ship's crew discovers new shared hostiles.</summary>
[ByRefEvent]
public readonly record struct WGCrewAlertEvent(EntityUid Grid, string Group, EntityUid[] Hostiles);

/// <summary>Broadcast once when a ship's shared crew alert ends.</summary>
[ByRefEvent]
public readonly record struct WGCrewAlertClearedEvent(EntityUid Grid, string Group);

/// <summary>Broadcast when two undocked grids collide, with each one's speed at the point and their closing speed.</summary>
[ByRefEvent]
public readonly record struct WGShipCollisionEvent(EntityUid Grid, EntityUid Other, float Speed, float OtherSpeed, float ClosingSpeed);

/// <summary>Broadcast when a ship weapon from another grid damages an anchored hull entity, or another grid rams it.</summary>
[ByRefEvent]
public readonly record struct WGCrewHullHitEvent(EntityUid Grid, EntityUid AttackerGrid);

/// <summary>
/// Raised on a crew's home grid when the crew turn on someone: an attacker, a thief, a trespasser past his warning.
/// <see cref="Sighting"/> marks hostility from the boarding rule alone, which a policy change can withdraw.
/// </summary>
[ByRefEvent]
public readonly record struct WGCrewAttackAnsweredEvent(string Group, EntityUid Attacker, bool Sighting = false);
