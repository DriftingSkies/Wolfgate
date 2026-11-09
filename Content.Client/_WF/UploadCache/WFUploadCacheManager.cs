using System.IO;
using Content.Shared._WF.UploadCache;
using Robust.Shared.ContentPack;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Utility;

namespace Content.Client._WF.UploadCache;

/// <summary>
/// Holds the server's shared files for the life of the client: in a content root under <see cref="WFUploadCache.Root"/>
/// and on disk by hash, so a later join (or a later launch) only fetches files it has never had.
/// </summary>
public sealed partial class WFUploadCacheManager
{
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private ITransferManager _transfer = default!;
    [Dependency] private ILogManager _logs = default!;

    private static readonly ResPath CacheDir = new("/wolfgate/upload_cache");

    private readonly MemoryContentRoot _root = new();
    private readonly object _lock = new();

    /// <summary>Mounted path to the hash it holds.</summary>
    private readonly Dictionary<ResPath, string> _mounted = new();

    /// <summary>Hashes asked for, to the paths waiting on them.</summary>
    private readonly Dictionary<string, List<ResPath>> _pending = new();

    private ISawmill _sawmill = default!;
    private bool _initialized;

    /// <summary>Once per client: entity systems are rebuilt on every connect, and a root or key can't be added twice.</summary>
    public void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        _sawmill = _logs.GetSawmill("wf.upload_cache");
        _resources.AddRoot(WFUploadCache.Root, _root);
        _transfer.RegisterTransferMessage(WFUploadCache.TransferKey, OnTransfer);
    }

    /// <summary>Whether a path under the root is mounted now.</summary>
    public bool IsMounted(ResPath relative)
    {
        lock (_lock)
        {
            return _mounted.ContainsKey(relative);
        }
    }

    /// <summary>Mounts what the list names from memory or disk, drops what it no longer names, and returns the rest.</summary>
    public List<string> Apply(WFUploadManifestEvent manifest)
    {
        var wanted = new List<string>();
        var listed = new HashSet<ResPath>();
        var hashes = new HashSet<string>();

        lock (_lock)
        {
            // Anything asked for under an older list is asked again if this one still names it.
            _pending.Clear();

            foreach (var entry in manifest.Files)
            {
                if (!WFUploadCache.IsHash(entry.Hash) || entry.Size < 0 || entry.Size > WFUploadCache.MaxFileBytes)
                    continue;

                var path = new ResPath(entry.Path);
                if (path.IsRooted || entry.Path.Contains(".."))
                    continue;

                listed.Add(path);
                hashes.Add(entry.Hash);

                if (_mounted.TryGetValue(path, out var have) && have == entry.Hash)
                    continue;

                if (TryReadCached(entry.Hash, entry.Size, out var data))
                {
                    Mount(path, entry.Hash, data);
                    continue;
                }

                if (!_pending.TryGetValue(entry.Hash, out var waiting))
                {
                    _pending[entry.Hash] = waiting = new List<ResPath>();
                    wanted.Add(entry.Hash);
                }

                if (!waiting.Contains(path))
                    waiting.Add(path);
            }

            foreach (var path in new List<ResPath>(_mounted.Keys))
            {
                if (listed.Contains(path))
                    continue;

                _mounted.Remove(path);
                _root.RemoveFile(path);
            }
        }

        PruneDisk(hashes);
        return wanted;
    }

    private void Mount(ResPath path, string hash, byte[] data)
    {
        _root.AddOrUpdateFile(path, data);
        _mounted[path] = hash;
    }

    private bool TryReadCached(string hash, int size, out byte[] data)
    {
        data = Array.Empty<byte>();
        var file = CacheDir / hash;
        try
        {
            if (!_resources.UserData.Exists(file))
                return false;

            using (var stream = _resources.UserData.OpenRead(file))
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                data = copy.ToArray();
            }

            if (data.Length == size && WFUploadCache.Hash(data) == hash)
                return true;

            _resources.UserData.Delete(file);
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Could not read cached file {hash}: {e.Message}");
        }

        return false;
    }

    private void WriteCached(string hash, byte[] data)
    {
        try
        {
            _resources.UserData.CreateDir(CacheDir);
            using var file = _resources.UserData.OpenWrite(CacheDir / hash);
            file.Write(data);
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Could not cache file {hash}; it will be fetched again next time: {e.Message}");
        }
    }

    /// <summary>Deletes cached files the server no longer lists, so the cache stays the size of what it shares.</summary>
    private void PruneDisk(HashSet<string> keep)
    {
        try
        {
            if (!_resources.UserData.IsDir(CacheDir))
                return;

            foreach (var name in new List<string>(_resources.UserData.DirectoryEntries(CacheDir)))
            {
                if (!keep.Contains(name))
                    _resources.UserData.Delete(CacheDir / name);
            }
        }
        catch (Exception e)
        {
            _sawmill.Warning($"Could not tidy the file cache: {e.Message}");
        }
    }

    private async void OnTransfer(TransferReceivedEvent transfer)
    {
        try
        {
            await using var stream = transfer.DataStream;
            await WFUploadCache.ReadFiles(stream, Receive);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Error while receiving shared files: {e}");
        }
    }

    /// <summary>Mounts and caches a file that arrived, if it is one this client asked for and its hash checks out.</summary>
    public void Receive(string hash, byte[] data)
    {
        if (!WFUploadCache.IsHash(hash) || WFUploadCache.Hash(data) != hash)
        {
            _sawmill.Warning($"Shared file {hash} arrived with the wrong contents; ignored.");
            return;
        }

        lock (_lock)
        {
            if (!_pending.Remove(hash, out var paths))
                return;

            foreach (var path in paths)
            {
                Mount(path, hash, data);
            }
        }

        WriteCached(hash, data);
    }
}
