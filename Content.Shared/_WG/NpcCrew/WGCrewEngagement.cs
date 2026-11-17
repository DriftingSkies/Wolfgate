using Robust.Shared.Prototypes;

namespace Content.Shared._WG.NpcCrew;

/// <summary>When a crewman leaves its duty to fight.</summary>
public enum WGCrewEngagement : byte
{
    /// <summary>Fights anyone its faction is hostile to, or that a crew alert hands it.</summary>
    OnSight,

    /// <summary>Fights only whoever attacked it, for as long as it remembers the attack.</summary>
    WhenAttacked,

    /// <summary>Never fights; runs for the bridge when the ship is alerted.</summary>
    Never,
}

/// <summary>Duty names. Each is a branch of the crew HTN root, picked by the WGCrewDuty blackboard key.</summary>
public static class WGCrewDuties
{
    public const string Guard = "Guard";
    public const string Pilot = "Pilot";
    public const string Gunnery = "Gunnery";
    public const string Janitor = "Janitor";
}

/// <summary>The role prototypes the planner hands out.</summary>
public static class WGCrewRoles
{
    public static readonly ProtoId<WGCrewRolePrototype> Deckhand = "WGCrewDeckhand";
    public static readonly ProtoId<WGCrewRolePrototype> Marine = "WGCrewMarine";
    public static readonly ProtoId<WGCrewRolePrototype> Pilot = "WGCrewPilot";
    public static readonly ProtoId<WGCrewRolePrototype> RadioOperator = "WGCrewRadioOperator";
    public static readonly ProtoId<WGCrewRolePrototype> Captain = "WGCrewCaptain";
    public static readonly ProtoId<WGCrewRolePrototype> Gunner = "WGCrewGunner";
    public static readonly ProtoId<WGCrewRolePrototype> Engineer = "WGCrewEngineer";
}
