using System;
using System.Collections.Generic;
using System.Reflection;
using Robust.Server.Upload;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Upload;
using Robust.Shared.Utility;

namespace Content.Server.Symphony;

/// <summary>
/// Puts a file the server made into the uploaded-resources channel that admin uploads use, so every client gets it
/// under /Uploaded and a player connecting later gets it as they join. The engine only takes uploads from a client,
/// so the store and the send are reached by reflection; when an engine update moves them, <see cref="Supported"/>
/// turns false and the panel is told, rather than anything failing mid-round.
/// </summary>
public static class SymphonyUploads
{
    /// <summary>Where uploaded files appear to clients and to the server's own resource manager.</summary>
    public static readonly ResPath Root = new("/Uploaded");

    private static readonly MethodInfo? StoreFile = typeof(SharedNetworkResourceManager).GetMethod(
        "StoreFile", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(ResPath), typeof(byte[])], null);

    private static readonly FieldInfo? ContentRootField = typeof(SharedNetworkResourceManager).GetField(
        "ContentRoot", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? SendToPlayer = typeof(NetworkResourceManager).GetMethod(
        "SendToPlayer", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>True while this engine build has the members the shim reaches for.</summary>
    public static bool Supported => StoreFile != null && ContentRootField != null && SendToPlayer != null;

    /// <summary>
    /// Store a file under <paramref name="relative"/> and send it to everyone connected. Hands back how many it was
    /// sent to. <paramref name="relative"/> is relative to <see cref="Root"/>, e.g. Symphony/Lobby/art/ab12.png.
    /// </summary>
    public static int Put(NetworkResourceManager manager, IServerNetManager net, ResPath relative, byte[] data)
    {
        if (!Supported)
            throw new NotSupportedException("this engine build does not let the server share a file with clients");
        StoreFile!.Invoke(manager, [relative, data]);
        var files = new List<(ResPath Relative, byte[] Data)> { (relative, data) };
        var sent = 0;
        foreach (var channel in net.Channels)
        {
            SendToPlayer!.Invoke(manager, [channel, files, 0]);
            sent++;
        }
        return sent;
    }

    /// <summary>
    /// Load a file without sending it: for start-up, when nobody is connected and everyone who joins gets it anyway.
    /// </summary>
    public static void Load(NetworkResourceManager manager, ResPath relative, byte[] data)
    {
        if (!Supported)
            throw new NotSupportedException("this engine build does not let the server share a file with clients");
        StoreFile!.Invoke(manager, [relative, data]);
    }

    /// <summary>
    /// Stop offering a file to players who join from now on. Clients that already have it keep it until they leave.
    /// </summary>
    public static void Remove(NetworkResourceManager manager, ResPath relative)
    {
        if (ContentRootField?.GetValue(manager) is MemoryContentRoot root)
            root.RemoveFile(relative);
    }
}
