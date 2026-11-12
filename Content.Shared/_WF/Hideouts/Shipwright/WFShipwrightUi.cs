using Robust.Shared.Serialization;

namespace Content.Shared._WF.Hideouts.Shipwright;

[Serializable, NetSerializable]
public enum WFShipwrightConsoleUiKey : byte
{
    Key,
}

/// <summary>What the shipwright console shows: the hull on the bay, its points, mounts and systems, and the job under way.</summary>
[Serializable, NetSerializable]
public sealed class WFShipwrightConsoleState : BoundUserInterfaceState
{
    /// <summary>The hull on the bay's pad, or null when the pad is empty.</summary>
    public WFShipwrightHullView? Hull;

    /// <summary>The cranes are working; the console waits.</summary>
    public bool Working;

    /// <summary>Items the job has done and has in all.</summary>
    public int Done;
    public int Total;

    /// <summary>Why the console can't work, as a loc id, such as no cranes linked.</summary>
    public string? Blocked;

    public int Balance;

    /// <summary>The bay's own refit point bonus, from the Shipwright module's levels.</summary>
    public int BayPoints;

    /// <summary>Whether the owner's ship slot is free for a new frame.</summary>
    public bool CanBuildFrame;

    /// <summary>The owner may work the console.</summary>
    public bool IsOwner;

    /// <summary>The console works a faction hangar: guns on the ship's own hardpoints and strongboxes only.</summary>
    public bool Faction;

    /// <summary>The drydock refit lines the console offers on the hull: armour plating, thruster, gyroscope and power tiers.</summary>
    public List<Content.Shared._WF.Drydock.WFDrydockUpgradeEntry> Upgrades = new();
}

/// <summary>A hull as the console sees it.</summary>
[Serializable, NetSerializable]
public sealed record WFShipwrightHullView(
    string Name,
    string? Frame,
    bool Registered,
    bool Yours,
    int Tiles,
    int Budget,
    int Spent,
    List<WFShipwrightMountView> Mounts,
    Dictionary<string, int> Systems,
    Dictionary<string, int> Unfitted,
    int UnfittedWeapons,
    int UnfittedPoints,
    bool Certified,
    NetEntity Grid,
    int MaxMounts,
    List<Vector2i> MountTiles);

/// <summary>A hardpoint: where it is, its prototype, size and type, whether it came with the frame, and the gun on it.</summary>
[Serializable, NetSerializable]
public sealed record WFShipwrightMountView(NetEntity Hardpoint, Vector2i Tile, string Proto, string Size, int Types, bool Stock,
    bool MountFitted, string? Gun, bool Fitted);

/// <summary>A hardpoint the console can lay: its prototype, size and type, points and price.</summary>
[Serializable, NetSerializable]
public sealed record WFShipwrightMountTypeView(string Id, string Name, int Size, int Types, int Points, int Price);

/// <summary>A hardpoint to lay on a free edge tile of the hull, with the gun to put on it, if any.</summary>
[Serializable, NetSerializable]
public sealed record WFShipwrightPlacement(Vector2i Tile, string Hardpoint, string Gun);

/// <summary>A gun the console can mount: its id, the sizes and types it fits, its points and price.</summary>
[Serializable, NetSerializable]
public sealed record WFShipwrightGunView(string Id, string Name, int Size, int Types, int Points, int Price);

/// <summary>The guns the console sells, sent once when it opens.</summary>
[Serializable, NetSerializable]
public sealed class WFShipwrightCatalogueMessage(List<WFShipwrightGunView> guns, List<WFShipwrightMountTypeView> mounts)
    : BoundUserInterfaceMessage
{
    public readonly List<WFShipwrightGunView> Guns = guns;
    public readonly List<WFShipwrightMountTypeView> Mounts = mounts;
}

/// <summary>The owner has the cranes build a frame on the empty pad.</summary>
[Serializable, NetSerializable]
public sealed class WFShipwrightBuildFrameMessage(string frame) : BoundUserInterfaceMessage
{
    public readonly string Frame = frame;
}

/// <summary>The owner has the cranes bring one of the hull's refit lines up a tier.</summary>
[Serializable, NetSerializable]
public sealed class WFShipwrightUpgradeMessage(string refit) : BoundUserInterfaceMessage
{
    public readonly string Refit = refit;
}

/// <summary>The owner registers the hull on the pad as their shipwright ship.</summary>
[Serializable, NetSerializable]
public sealed class WFShipwrightRegisterMessage : BoundUserInterfaceMessage;

/// <summary>The owner pays to have every hand-built part on the hull looked over and counted as fitted.</summary>
[Serializable, NetSerializable]
public sealed class WFShipwrightCertifyMessage : BoundUserInterfaceMessage;

/// <summary>
/// The owner orders a refit: each hardpoint's gun (an empty string clears it), hardpoints swapped for another type (an
/// empty string takes the hardpoint off), new hardpoints laid on free edge tiles, and each system's count. Anything left
/// out stays as it is.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFShipwrightRefitMessage(
    Dictionary<NetEntity, string> mounts,
    Dictionary<string, int> systems,
    Dictionary<NetEntity, string> hardpoints,
    List<WFShipwrightPlacement> placements) : BoundUserInterfaceMessage
{
    public readonly Dictionary<NetEntity, string> Mounts = mounts;
    public readonly Dictionary<string, int> Systems = systems;
    public readonly Dictionary<NetEntity, string> Hardpoints = hardpoints;
    public readonly List<WFShipwrightPlacement> Placements = placements;
}
