using Robust.Shared.Serialization;

namespace Content.Shared._WG.MappingMode;

/// <summary>
/// One grid of the mapper's session: its name, tiles and entities on it, contents included, and the level it is on.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WGMappingGridInfo(
    NetEntity Grid,
    string Name,
    int Tiles,
    int Entities,
    int Depth = 0);

/// <summary>Asks for the grids on every level of the mapper's session.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingGridListRequest : EntityEventArgs;

/// <summary>Server to client: the grids on every level of the mapper's session, top level first, largest first.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingGridListEvent(List<WGMappingGridInfo> grids) : EntityEventArgs
{
    public readonly List<WGMappingGridInfo> Grids = grids;
}

/// <summary>Moves the mapper to the centre of a grid on one of their session's maps.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingGoToGridRequest(NetEntity grid) : EntityEventArgs
{
    public readonly NetEntity Grid = grid;
}

/// <summary>Deletes a whole grid on one of the mapper's session maps, as one undoable edit.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingDeleteGridRequest(NetEntity grid) : EntityEventArgs
{
    public readonly NetEntity Grid = grid;
}

/// <summary>
/// Turns on or off starting a new grid with the next tiles placed off every grid; otherwise they join the nearest grid.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGMappingNewGridRequest(bool enabled) : EntityEventArgs
{
    public readonly bool Enabled = enabled;
}

/// <summary>Server to client: whether the next tiles placed off every grid start a new grid.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingNewGridStateEvent(bool enabled) : EntityEventArgs
{
    public readonly bool Enabled = enabled;
}
