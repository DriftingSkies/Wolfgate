using Robust.Shared.Serialization;

namespace Content.Shared._WG.MappingMode;

/// <summary>The autosave intervals a session owner can pick, in minutes; 0 turns autosave off.</summary>
public static class WGMappingAutosaveSteps
{
    public static readonly int[] Minutes = { 0, 1, 2, 5, 10, 15 };

    public static bool IsStep(int minutes)
    {
        return Array.IndexOf(Minutes, minutes) >= 0;
    }
}

/// <summary>
/// One saved version of a library map. <see cref="Ship"/> is set when the version holds exactly one grid and its grid
/// file was saved, so it downloads as a ship.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct WGMappingLibraryVersion(WGMappingVersionName Name, int Levels, long Bytes, bool Ship);

/// <summary>
/// A map in a player's library and its versions, newest first, with the ship details the mapper last gave it (from the
/// newest version that keeps a <c>ship.yml</c>), so the export wizard pre-fills.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGMappingLibraryMap(string name, List<WGMappingLibraryVersion> versions,
    Export.WGMappingShipDetails? ship = null)
{
    public readonly string Name = name;
    public readonly List<WGMappingLibraryVersion> Versions = versions;
    public readonly Export.WGMappingShipDetails? Ship = ship;
}

/// <summary>Asks for the player's library.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingLibraryRequest : EntityEventArgs;

/// <summary>Saves the player's session into their own library under a name; empty means the session's name.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingSaveRequest(string name) : EntityEventArgs
{
    public readonly string Name = name;
}

/// <summary>Opens a version of a map from the player's library as a new session; null means the newest.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingOpenRequest(string mapName, string? version) : EntityEventArgs
{
    public readonly string MapName = mapName;
    public readonly string? Version = version;
}

/// <summary>Deletes a version of a library map, or the whole map when the version is null.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingDeleteRequest(string mapName, string? version) : EntityEventArgs
{
    public readonly string MapName = mapName;
    public readonly string? Version = version;
}

/// <summary>The session owner picks the autosave interval, one of <see cref="WGMappingAutosaveSteps.Minutes"/>.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingAutosaveStepRequest(int minutes) : EntityEventArgs
{
    public readonly int Minutes = minutes;
}

/// <summary>Server to client: the player's library, maps by name, and its usage against the caps.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingLibraryEvent(List<WGMappingLibraryMap> maps, long bytes, long maxBytes, int maxMaps,
    int maxVersions) : EntityEventArgs
{
    public readonly List<WGMappingLibraryMap> Maps = maps;
    public readonly long Bytes = bytes;
    public readonly long MaxBytes = maxBytes;
    public readonly int MaxMaps = maxMaps;
    public readonly int MaxVersions = maxVersions;
}

/// <summary>
/// Downloads a version of a library map, null meaning the newest: every level, or with <see cref="Ship"/> its one grid
/// and the generated vessel prototype.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGMappingDownloadRequest(string mapName, string? version, bool ship = false) : EntityEventArgs
{
    public readonly string MapName = mapName;
    public readonly string? Version = version;
    public readonly bool Ship = ship;
}

/// <summary>Stops the player's download.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingDownloadCancelRequest : EntityEventArgs;

/// <summary>
/// Server to client: one chunk of a downloaded level file. A transfer is one file; a version's files arrive one
/// transfer after another, <see cref="FileIndex"/> of <see cref="FileCount"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGMappingDownloadChunkEvent(
    int transferId,
    string mapName,
    string version,
    string fileName,
    int fileIndex,
    int fileCount,
    int chunk,
    int chunkCount,
    int totalBytes,
    byte[] data) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly string MapName = mapName;
    public readonly string Version = version;
    public readonly string FileName = fileName;
    public readonly int FileIndex = fileIndex;
    public readonly int FileCount = fileCount;
    public readonly int Chunk = chunk;
    public readonly int ChunkCount = chunkCount;

    /// <summary>The whole file's size, which the client checks what it wrote against.</summary>
    public readonly int TotalBytes = totalBytes;

    public readonly byte[] Data = data;
}

/// <summary>Client to server: a chunk arrived; the server sends the next.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingDownloadAckEvent(int transferId, int chunk) : EntityEventArgs
{
    public readonly int TransferId = transferId;
    public readonly int Chunk = chunk;
}

/// <summary>Server to client: the download stopped before it finished; partial files should be dropped.</summary>
[Serializable, NetSerializable]
public sealed class WGMappingDownloadCancelledEvent(int transferId) : EntityEventArgs
{
    public readonly int TransferId = transferId;
}
