using Content.Server._WF.UploadCache;
using Content.Shared._WF.UploadCache;
using Robust.Shared.Utility;

namespace Content.Server.Symphony;

/// <summary>
/// Shares a file the server made with clients under /Uploaded. It goes through Wolfgate's upload cache: a joining
/// client is sent only the files it has not kept on disk from an earlier join, not every file every time.
/// </summary>
public static class SymphonyUploads
{
    /// <summary>Where uploaded files appear to clients and to the server's own resource manager.</summary>
    public static readonly ResPath Root = WFUploadCache.Root;

    /// <summary>Always true now; the panel still asks, from when sharing went through engine internals.</summary>
    public static bool Supported => true;

    /// <summary>
    /// Share a file under <paramref name="relative"/> and tell everyone connected. Hands back how many were told.
    /// <paramref name="relative"/> is relative to <see cref="Root"/>, e.g. Symphony/Lobby/art/ab12.png.
    /// </summary>
    public static int Put(WFUploadCacheSystem cache, ResPath relative, byte[] data)
    {
        return cache.Put(relative, data);
    }

    /// <summary>Share a file without telling anyone: for start-up, when everyone who joins gets the list anyway.</summary>
    public static void Load(WFUploadCacheSystem cache, ResPath relative, byte[] data)
    {
        cache.Load(relative, data);
    }

    /// <summary>Stop sharing a file.</summary>
    public static void Remove(WFUploadCacheSystem cache, ResPath relative)
    {
        cache.Remove(relative);
    }
}
