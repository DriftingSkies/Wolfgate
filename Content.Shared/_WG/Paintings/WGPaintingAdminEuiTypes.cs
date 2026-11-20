using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Paintings;

[Serializable, NetSerializable]
public enum WGPaintingAdminFilter : byte
{
    All,
    Visible,
    Blocked,
}

/// <summary>A catalogue painting with every stored value, as the admin browser lists it.</summary>
[Serializable, NetSerializable]
public sealed record WGPaintingAdminEntry(
    string Hash,
    int Width,
    int Height,
    string Title,
    string Artist,
    Guid? Author,
    string AuthorName,
    Guid ScannedBy,
    string ScannerName,
    DateTime CreatedAt,
    bool Blocked);

/// <summary>The search results, and the painting picked for a closer look.</summary>
[Serializable, NetSerializable]
public sealed class WGPaintingAdminEuiState : EuiStateBase
{
    public string Query = string.Empty;
    public WGPaintingAdminFilter Filter;
    public List<WGPaintingAdminEntry> Results = new();

    /// <summary>Matches in all, of which <see cref="Results"/> holds the newest few.</summary>
    public int Total;

    public bool Loaded;
    public WGPaintingAdminEntry? Selected;

    /// <summary>The selected painting's pixels, null while they load or when it has none.</summary>
    public byte[]? Pixels;
}

[Serializable, NetSerializable]
public sealed class WGPaintingAdminSearchMessage(string query, WGPaintingAdminFilter filter) : EuiMessageBase
{
    public readonly string Query = query;
    public readonly WGPaintingAdminFilter Filter = filter;
}

[Serializable, NetSerializable]
public sealed class WGPaintingAdminSelectMessage(string hash) : EuiMessageBase
{
    public readonly string Hash = hash;
}

[Serializable, NetSerializable]
public sealed class WGPaintingAdminBlockMessage(string hash, bool blocked) : EuiMessageBase
{
    public readonly string Hash = hash;
    public readonly bool Blocked = blocked;
}

/// <summary>Deletes a painting from the database and every copy of it in the round.</summary>
[Serializable, NetSerializable]
public sealed class WGPaintingAdminDeleteMessage(string hash) : EuiMessageBase
{
    public readonly string Hash = hash;
}
