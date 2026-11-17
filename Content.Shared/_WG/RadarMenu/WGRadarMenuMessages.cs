using Robust.Shared.Serialization;

namespace Content.Shared._WG.RadarMenu;

/// <summary>A free dock on another grid that the helm's ship can dock with.</summary>
[Serializable, NetSerializable]
public readonly record struct WGDockOption(NetEntity Dock, string Name, float Distance);

/// <summary>The helm asks which docks on a grid its ship could dock with.</summary>
[Serializable, NetSerializable]
public sealed class WGAutoDockOptionsRequestMessage(NetEntity grid) : BoundUserInterfaceMessage
{
    public NetEntity Grid = grid;
}

/// <summary>The docks on a grid the helm's ship could dock with now, nearest first. Sent to the pilot who asked.</summary>
[Serializable, NetSerializable]
public sealed class WGAutoDockOptionsEvent(NetEntity console, NetEntity grid, List<WGDockOption> docks) : EntityEventArgs
{
    public NetEntity Console = console;
    public NetEntity Grid = grid;
    public List<WGDockOption> Docks = docks;
}

/// <summary>Fly to a grid and dock: at one dock, or at the nearest free one that fits when <see cref="Dock"/> is null.</summary>
[Serializable, NetSerializable]
public sealed class WGAutoDockRequestMessage(NetEntity grid, NetEntity? dock) : BoundUserInterfaceMessage
{
    public NetEntity Grid = grid;
    public NetEntity? Dock = dock;
}

/// <summary>
/// Fly toward a world and enter its orbit once in range. An unsanctioned world needs <see cref="Confirmed"/>, as the
/// console's orbit button asks.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGAutoOrbitRequestMessage(NetEntity planet, bool confirmed) : BoundUserInterfaceMessage
{
    public NetEntity Planet = planet;
    public bool Confirmed = confirmed;
}

/// <summary>Switch the helm's autopilot off, and any docking it was flying to.</summary>
[Serializable, NetSerializable]
public sealed class WGAutopilotCancelMessage : BoundUserInterfaceMessage;
