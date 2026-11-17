using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Ghost;

/// <summary>
/// Sections of the ghost orbit menu, in display order.
/// </summary>
[Serializable, NetSerializable]
public enum GhostOrbitCategory : byte
{
    Interest,
    Antagonist,
    Critical,
    Alive,
    Xenonid,
    Disconnected,
    Dead,
    Ghost,
    Location,
    PlanetPoi,
    Ship,
    Npc,
}

/// <summary>
/// One thing a ghost can orbit.
/// </summary>
[Serializable, NetSerializable]
public record struct GhostOrbitTarget
{
    public NetEntity Entity;
    public string Name;
    public GhostOrbitCategory Category;

    /// <summary>Job name, or what kind of point of interest this is.</summary>
    public string? Detail;

    /// <summary>Prototype the client draws an icon from when there is no job icon.</summary>
    public string? Prototype;

    public ProtoId<JobIconPrototype>? JobIcon;

    /// <summary>The company rank whose custom job icon replaces <see cref="JobIcon"/>, or 0.</summary>
    public int CompanyRank;

    /// <summary>Damage as a fraction of the dead threshold, 0 to 1. Null when the target has no health.</summary>
    public float? Damage;

    /// <summary>Ghosts currently following the target, admins excluded for non-admin viewers.</summary>
    public int Followers;

    /// <summary>Player mind with no client attached (SSD or catatonic).</summary>
    public bool Disconnected;

    /// <summary>Only listed because the viewer is an admin (admin warp points, admin ghosts).</summary>
    public bool AdminOnly;

    /// <summary>Sort rank inside the section, lowest first. Only the Xenonids section uses it.</summary>
    public int Order;

    /// <summary>The faction of a player or ship, by its company's family, or null for none.</summary>
    public string? Faction;

    /// <summary>The faction's colour, for its section in the Faction view.</summary>
    public Color? FactionColor;

    /// <summary>Where the target is: the sector, a world, or in transit. For the Place view.</summary>
    public string? Place;

    /// <summary>Sort rank of <see cref="Place"/>: the sector first, then worlds, then transit and the rest.</summary>
    public int PlaceOrder;
}

/// <summary>
/// Client asks for everything it can orbit. Answered with <see cref="GhostOrbitTargetsEvent"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOrbitRequestEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class GhostOrbitTargetsEvent(List<GhostOrbitTarget> targets, int burrowedLarva = 0) : EntityEventArgs
{
    public List<GhostOrbitTarget> Targets = targets;

    /// <summary>Burrowed larva waiting across all hives.</summary>
    public int BurrowedLarva = burrowedLarva;
}

/// <summary>
/// Client asks to orbit a target. Anything that moves is followed; fixed warp points are teleported to.
/// </summary>
[Serializable, NetSerializable]
public sealed class GhostOrbitWarpEvent(NetEntity target) : EntityEventArgs
{
    public NetEntity Target = target;
}
