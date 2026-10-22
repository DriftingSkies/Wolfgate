using System.Numerics;
using Content.Shared._WF.MappingTools;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MappingMode;

/// <summary>Asks for the files a mapper can load as a reference grid.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceListRequest : EntityEventArgs;

/// <summary>Server to client: the files a mapper can load as a reference grid.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceListEvent(List<MappingMapFile> files) : EntityEventArgs
{
    public readonly List<MappingMapFile> Files = files;
}

/// <summary>Loads a listed file aside and asks for its footprint, to place it.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferencePickRequest(string path) : EntityEventArgs
{
    public readonly string Path = path;
}

/// <summary>
/// Server to client: the footprint of a picked reference, relative to its anchor, under a token that confirms or
/// cancels it.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceFootprintEvent(int token, List<WFMappingFootprintGrid> grids) : EntityEventArgs
{
    public readonly int Token = token;
    public readonly List<WFMappingFootprintGrid> Grids = grids;
}

/// <summary>
/// Places the picked reference with its anchor at <see cref="Offset"/> on the mapper's level, turned clockwise by
/// <see cref="Turns"/> quarter turns.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceConfirmRequest(int token, Vector2 offset, int turns) : EntityEventArgs
{
    public readonly int Token = token;
    public readonly Vector2 Offset = offset;
    public readonly int Turns = turns;
}

/// <summary>Drops the picked reference.</summary>
[Serializable, NetSerializable]
public sealed class WFMappingReferenceCancelRequest(int token) : EntityEventArgs
{
    public readonly int Token = token;
}
