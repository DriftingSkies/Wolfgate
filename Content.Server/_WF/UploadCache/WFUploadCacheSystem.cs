using System.IO;
using System.Threading.Tasks;
using Content.Server.GameTicking;
using Content.Shared._WF.UploadCache;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.UploadCache;

/// <summary>
/// Shares files made at runtime with clients, which keep them on disk across joins. A file is mounted under
/// <see cref="WFUploadCache.Root"/> for the server's own resource manager; clients get the list on joining the lobby
/// and whenever it changes, and are sent only the files they ask for.
/// </summary>
public sealed partial class WFUploadCacheSystem : EntitySystem
{
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private ITransferManager _transfer = default!;

    private readonly MemoryContentRoot _root = new();
    private readonly Dictionary<ResPath, (string Hash, byte[] Data)> _files = new();
    private readonly Dictionary<string, byte[]> _byHash = new();
    private bool _mounted;

    public override void Initialize()
    {
        base.Initialize();
        EnsureMounted();
        _transfer.RegisterTransferMessage(WFUploadCache.TransferKey);

        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(ev => SendManifest(ev.PlayerSession));
        SubscribeNetworkEvent<WFUploadRequestEvent>(OnRequest);
    }

    /// <summary>Other systems may load files in their own Initialize, before this one's.</summary>
    private void EnsureMounted()
    {
        if (_mounted)
            return;

        _mounted = true;
        _resources.AddRoot(WFUploadCache.Root, _root);
    }

    /// <summary>The paths shared now, relative to the root.</summary>
    public IEnumerable<ResPath> Files => _files.Keys;

    /// <summary>
    /// Shares a file under <paramref name="relative"/> and tells everyone connected. Hands back how many were told.
    /// </summary>
    public int Put(ResPath relative, byte[] data)
    {
        Load(relative, data);
        return BroadcastManifest();
    }

    /// <summary>Shares a file without telling anyone yet: for start-up, when the lobby list goes out on joining.</summary>
    public void Load(ResPath relative, byte[] data)
    {
        if (data.Length > WFUploadCache.MaxFileBytes)
            throw new ArgumentException($"{relative} is over {WFUploadCache.MaxFileBytes} bytes");

        EnsureMounted();
        if (_files.TryGetValue(relative, out var old))
            Forget(relative, old.Hash);

        var hash = WFUploadCache.Hash(data);
        _files[relative] = (hash, data);
        _byHash[hash] = data;
        _root.AddOrUpdateFile(relative, data);
    }

    /// <summary>Stops sharing a file. Clients drop it from memory at the next list, and from disk at the one after.</summary>
    public void Remove(ResPath relative)
    {
        if (!_files.TryGetValue(relative, out var entry))
            return;

        Forget(relative, entry.Hash);
        BroadcastManifest();
    }

    private void Forget(ResPath relative, string hash)
    {
        _files.Remove(relative);
        _root.RemoveFile(relative);

        var stillUsed = false;
        foreach (var (_, other) in _files)
        {
            if (other.Hash == hash)
            {
                stillUsed = true;
                break;
            }
        }

        if (!stillUsed)
            _byHash.Remove(hash);
    }

    private WFUploadManifestEvent BuildManifest()
    {
        var manifest = new WFUploadManifestEvent();
        foreach (var (path, (hash, data)) in _files)
        {
            manifest.Files.Add(new WFUploadEntry { Path = path.ToString(), Hash = hash, Size = data.Length });
        }

        return manifest;
    }

    private void SendManifest(ICommonSession session)
    {
        RaiseNetworkEvent(BuildManifest(), session);
    }

    private int BroadcastManifest()
    {
        var manifest = BuildManifest();
        var sent = 0;
        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            RaiseNetworkEvent(manifest, session);
            sent++;
        }

        return sent;
    }

    private void OnRequest(WFUploadRequestEvent ev, EntitySessionEventArgs args)
    {
        var files = new List<(string Hash, byte[] Data)>();
        var seen = new HashSet<string>();
        foreach (var hash in ev.Hashes)
        {
            if (seen.Add(hash) && _byHash.TryGetValue(hash, out var data))
                files.Add((hash, data));
        }

        if (files.Count > 0)
            Send(args.SenderSession, files);
    }

    private async void Send(ICommonSession session, List<(string Hash, byte[] Data)> files)
    {
        Stream stream;
        try
        {
            stream = _transfer.StartTransfer(session.Channel,
                new TransferStartInfo { MessageKey = WFUploadCache.TransferKey });
        }
        catch (InvalidOperationException)
        {
            // No transfer channel for this player (never set up, or gone): the game connection carries them.
            var ev = new WFUploadFilesEvent();
            foreach (var (hash, data) in files)
            {
                ev.Hashes.Add(hash);
                ev.Data.Add(data);
            }

            RaiseNetworkEvent(ev, session);
            return;
        }

        try
        {
            await using (stream)
            {
                await WFUploadCache.WriteFiles(stream, files);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Could not send {files.Count} shared files to {session}: {e.Message}");
        }
    }
}
