using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WG.GameHub;
using Robust.Shared.Asynchronous;
using Robust.Shared.ContentPack;
using Robust.Shared.Network.Transfer;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WG.GameHub;

public enum WGGameDataStatus : byte
{
    Unknown,
    Missing,
    Downloading,
    Ready,
    Failed,
}

/// <summary>Receives game data from the server and keeps it in the player's user data between sessions.</summary>
public sealed partial class WGGameDataManager
{
    private static readonly ResPath Folder = new("/wolfgate_games");

    [Dependency] private ILogManager _log = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private ITaskManager _task = default!;
    [Dependency] private ITransferManager _transfer = default!;

    private readonly Dictionary<string, Entry> _entries = new();
    private ISawmill _sawmill = default!;

    public void Initialize()
    {
        _sawmill = _log.GetSawmill("gamehub");
        _transfer.RegisterTransferMessage(WGGameHubProtocol.TransferKey, OnTransfer);
    }

    public WGGameDataStatus GetStatus(string game) => GetEntry(game).Status;

    public (long Received, long Total) GetProgress(string game)
    {
        var entry = GetEntry(game);
        return (Interlocked.Read(ref entry.Received), entry.Total);
    }

    /// <summary>The server said what the current data is; decides whether this client has it.</summary>
    public void SetManifest(string game, long size, string version)
    {
        var entry = GetEntry(game);
        entry.Total = size;
        entry.Version = version;

        if (entry.Status == WGGameDataStatus.Downloading)
            return;

        entry.Status = HasVersion(game, size, version) ? WGGameDataStatus.Ready : WGGameDataStatus.Missing;
    }

    public void BeginDownload(string game)
    {
        _resources.UserData.CreateDir(Folder);

        var entry = GetEntry(game);
        entry.Status = WGGameDataStatus.Downloading;
        Interlocked.Exchange(ref entry.Received, 0);
    }

    public void SetFailed(string game)
    {
        GetEntry(game).Status = WGGameDataStatus.Failed;
    }

    /// <summary>Opens a downloaded game's data. Only call when its status is Ready.</summary>
    public Stream OpenData(string game)
    {
        return _resources.UserData.OpenRead(DataPath(game));
    }

    private Entry GetEntry(string game)
    {
        lock (_entries)
        {
            if (!_entries.TryGetValue(game, out var entry))
                _entries[game] = entry = new Entry();

            return entry;
        }
    }

    private ResPath DataPath(string game)
    {
        return Folder / _protos.Index<WGGamePrototype>(game).DataFile;
    }

    private bool HasVersion(string game, long size, string version)
    {
        var data = _resources.UserData;
        var path = DataPath(game);
        if (!data.Exists(path) || !data.TryReadAllText(VersionPath(path), out var stored))
            return false;

        using var file = data.OpenRead(path);
        return file.Length == size && stored == version;
    }

    private static ResPath VersionPath(ResPath data) => data.WithName(string.Concat(data.Filename, ".version"));

    private async void OnTransfer(TransferReceivedEvent ev)
    {
        await Receive(ev.DataStream);
    }

    /// <summary>Reads one game data stream through to the player's game folder.</summary>
    public async Task Receive(Stream source)
    {
        string? game = null;
        try
        {
            await using var stream = source;

            var lengthBytes = new byte[4];
            await stream.ReadExactlyAsync(lengthBytes);
            var headerLength = BitConverter.ToInt32(lengthBytes, 0);
            if (headerLength is <= 0 or > WGGameHubProtocol.MaxHeaderBytes)
                throw new IOException("Bad game data header.");

            var headerBytes = new byte[headerLength];
            await stream.ReadExactlyAsync(headerBytes);

            using var reader = new BinaryReader(new MemoryStream(headerBytes));
            game = reader.ReadString();
            var size = reader.ReadInt64();
            var version = reader.ReadString();

            var entry = GetEntry(game);
            var target = DataPath(game);
            entry.Total = size;

            var part = target.WithName(string.Concat(target.Filename, ".part"));
            await using (var file = _resources.UserData.OpenWrite(part))
            {
                var chunk = new byte[81920];
                long received = 0;
                int read;
                while ((read = await stream.ReadAsync(chunk)) > 0)
                {
                    received += read;
                    if (received > size)
                        throw new IOException("Game data is larger than the server said.");

                    await file.WriteAsync(chunk.AsMemory(0, read));
                    Interlocked.Exchange(ref entry.Received, received);
                }

                if (received != size)
                    throw new IOException("Game data ended early.");
            }

            var finished = game;
            _task.RunOnMainThread(() => Finish(finished, part, target, version, size));
        }
        catch (Exception e)
        {
            var failed = game;
            _task.RunOnMainThread(() =>
            {
                _sawmill.Warning($"Game data download failed: {e.Message}");
                if (failed != null)
                    SetFailed(failed);
            });
        }
    }

    private void Finish(string game, ResPath part, ResPath target, string version, long size)
    {
        var data = _resources.UserData;
        if (data.Exists(target))
            data.Delete(target);

        data.Rename(part, target);
        data.WriteAllText(VersionPath(target), version);
        SetManifest(game, size, version);
        GetEntry(game).Status = WGGameDataStatus.Ready;
    }

    private sealed class Entry
    {
        public WGGameDataStatus Status;
        public long Received;
        public long Total;
        public string? Version;
    }
}
