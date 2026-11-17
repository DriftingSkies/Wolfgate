using Robust.Shared.Serialization;

namespace Content.Shared._WG.Drydock;

/// <summary>UI key for the drydock terminal.</summary>
[Serializable, NetSerializable]
public enum WGDrydockTerminalUiKey : byte
{
    Key,
}

/// <summary>One berth a terminal works: its console, its name, whether it is locked, and its console's state.</summary>
[Serializable, NetSerializable]
public sealed class WGDrydockTerminalBerth
{
    /// <summary>The berth's console.</summary>
    public NetEntity Console;

    /// <summary>The berth's name.</summary>
    public string Label = string.Empty;

    /// <summary>Whether the berth is locked to walk-in work and bookings.</summary>
    public bool Locked;

    /// <summary>The berth console's state, with the viewer's own prices.</summary>
    public WGDrydockConsoleState State = new();
}

/// <summary>A terminal's state for one viewer: the berths, and what this viewer may do with them.</summary>
[Serializable, NetSerializable]
public sealed class WGDrydockTerminalState
{
    /// <summary>The berths linked to the terminal, by name.</summary>
    public List<WGDrydockTerminalBerth> Berths = new();

    /// <summary>The viewer may start and stop work at the berths.</summary>
    public bool CanOperate;

    /// <summary>The viewer may lock and unlock berths.</summary>
    public bool CanLock;

    /// <summary>The viewer may set the drydock price.</summary>
    public bool CanConfigure;

    /// <summary>The terminal stands on a commanded ship, whose price rules set what drydock work costs.</summary>
    public bool Commanded;

    /// <summary>The ship's drydock price rule's multiplier while it is on; null for the usual price.</summary>
    public float? Multiplier;
}

/// <summary>A terminal's state, sent to the one viewer it was built for.</summary>
[Serializable, NetSerializable]
public sealed class WGDrydockTerminalStateMessage(WGDrydockTerminalState state) : BoundUserInterfaceMessage
{
    /// <summary>The viewer's state.</summary>
    public readonly WGDrydockTerminalState State = state;
}

/// <summary>Sent by the window once it is open.</summary>
[Serializable, NetSerializable]
public sealed class WGDrydockTerminalRefreshMessage : BoundUserInterfaceMessage;

/// <summary>Lock or unlock one berth, or every berth when <see cref="Berth"/> is null.</summary>
[Serializable, NetSerializable]
public sealed class WGDrydockTerminalLockMessage(NetEntity? berth, bool locked) : BoundUserInterfaceMessage
{
    /// <summary>The berth console to lock or unlock, or null for all of them.</summary>
    public readonly NetEntity? Berth = berth;

    /// <summary>True to lock, false to unlock.</summary>
    public readonly bool Locked = locked;
}

/// <summary>Set the ship's drydock price multiplier (0 free, 1 the usual price), or null to drop it.</summary>
[Serializable, NetSerializable]
public sealed class WGDrydockTerminalPriceMessage(float? multiplier) : BoundUserInterfaceMessage
{
    /// <summary>The multiplier, or null for the usual price.</summary>
    public readonly float? Multiplier = multiplier;
}
