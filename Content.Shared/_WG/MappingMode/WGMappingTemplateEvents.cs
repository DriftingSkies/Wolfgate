using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.MappingMode;

/// <summary>
/// Where a shared template sits, the same for every member: its centre in world tiles, scale, pixels per tile,
/// rotation in degrees, layer and z-level. Opacity, show and lock stay with each player.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WGMappingTemplateState(
    float X,
    float Y,
    float ScaleX,
    float ScaleY,
    float Rotation,
    int PixelsPerTile,
    WGMappingTemplateLayer Layer,
    int Depth);

/// <summary>A session's shared template as its members see it; <see cref="Id"/> changes with every share.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateInfo(int id, NetUserId sharedBy, string sharedByName, int width, int height,
    int bytes, WGMappingTemplateState state)
{
    public readonly int Id = id;
    public readonly NetUserId SharedBy = sharedBy;
    public readonly string SharedByName = sharedByName;
    public readonly int Width = width;
    public readonly int Height = height;
    public readonly int Bytes = bytes;
    public readonly WGMappingTemplateState State = state;
}

/// <summary>
/// Shares a PNG of <see cref="Bytes"/> bytes in <see cref="ChunkCount"/> chunks with the player's session, placed at
/// <see cref="State"/>. The server answers <see cref="WGMappingTemplateUploadReadyEvent"/> or refuses.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateUploadBeginRequest(int bytes, int chunkCount, WGMappingTemplateState state)
    : EntityEventArgs
{
    public readonly int Bytes = bytes;
    public readonly int ChunkCount = chunkCount;
    public readonly WGMappingTemplateState State = state;
}

/// <summary>Server to client: the share is accepted; send its chunks under this transfer id.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateUploadReadyEvent(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>Client to server: one chunk of the PNG being shared.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateUploadChunkEvent(int transferId, int chunk, byte[] data) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int Chunk = chunk;
    public readonly byte[] Data = data;
}

/// <summary>Server to client: a chunk arrived; the client sends the next.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateUploadAckEvent(int transferId, int chunk) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int Chunk = chunk;
}

/// <summary>Server to client: the PNG is now the session's shared template <see cref="TemplateId"/>.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateUploadDoneEvent(int transferId, int templateId, WGMappingTemplateState state)
    : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int TemplateId = templateId;
    public readonly WGMappingTemplateState State = state;
}

/// <summary>Stops the player's running share.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateUploadCancelRequest(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>
/// Server to client: the share was refused or stopped; 0 for a refused begin. A <see cref="WGMappingResultEvent"/>
/// says why.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateUploadCancelledEvent(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}

/// <summary>Server to client: one chunk of the session's shared template, <see cref="Bytes"/> bytes in all.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateDownloadChunkEvent(int templateId, int bytes, int chunkCount, int chunk,
    byte[] data) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
    public readonly int Bytes = bytes;
    public readonly int ChunkCount = chunkCount;
    public readonly int Chunk = chunk;
    public readonly byte[] Data = data;
}

/// <summary>Client to server: a chunk of the shared template arrived.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateDownloadAckEvent(int templateId, int chunk) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
    public readonly int Chunk = chunk;
}

/// <summary>Moves, scales or turns the session's shared template; the server clamps it.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateTransformRequest(int templateId, WGMappingTemplateState state) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
    public readonly WGMappingTemplateState State = state;
}

/// <summary>Server to client: where the shared template sits now.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateTransformEvent(int templateId, WGMappingTemplateState state) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
    public readonly WGMappingTemplateState State = state;
}

/// <summary>Removes the session's shared template for everyone, if it is still <see cref="TemplateId"/>.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingTemplateUnshareRequest(int templateId) : EntityEventArgs
{
    public readonly int TemplateId = templateId;
}
