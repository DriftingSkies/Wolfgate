using System.IO;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Shared._WF.MappingMode;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// Streams a library version's level files, its ship grid and vessel prototype, or an export package built in memory,
/// to their owner: one transfer per file and one download per player, in chunks paced by the client's acknowledgements.
/// </summary>
public sealed class WFMappingDownloadSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private WFMappingLibrarySystem _library = default!;
    [Dependency] private WFMappingModeSystem _mapping = default!;

    /// <summary>Chunks sent ahead of the last acknowledged one.</summary>
    private const int InFlight = 2;

    /// <summary>A download whose client acknowledges nothing for this long is dropped.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A file a download sends: read from the data folder, or already in memory.</summary>
    public readonly record struct DownloadFile(string Name, ResPath? Path, byte[]? Bytes);

    /// <summary>One player's download: the version's files and where the current one stands.</summary>
    private sealed class Download(ICommonSession player, string mapName, string version, List<DownloadFile> files)
    {
        public readonly ICommonSession Player = player;
        public readonly string MapName = mapName;
        public readonly string Version = version;
        public readonly List<DownloadFile> Files = files;

        public int FileIndex = -1;
        public int TransferId;
        public byte[] Data = Array.Empty<byte>();
        public int ChunkCount;
        public int NextChunk;
        public int Acked;
        public TimeSpan LastActivity;
    }

    private readonly Dictionary<NetUserId, Download> _downloads = new();
    private readonly WFMappingThrottle<NetUserId> _requestThrottle = new(1, 1);
    private int _nextTransferId = 1;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingDownloadRequest>(OnRequest);
        SubscribeNetworkEvent<WFMappingDownloadAckEvent>(OnAck);
        SubscribeNetworkEvent<WFMappingDownloadCancelRequest>(OnCancelRequest);
        SubscribeLocalEvent<WFMappingMembershipEndedEvent>(OnMembershipEnded);

        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_downloads.Count == 0)
            return;

        var now = _timing.RealTime;
        foreach (var (user, download) in _downloads.ToList())
        {
            if (now - download.LastActivity > StallTimeout)
                Cancel(user, "wf-mapping-mode-download-stalled");
        }
    }

    private void OnRequest(WFMappingDownloadRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_requestThrottle.TryTake(player.UserId, _timing.CurTime))
            return;

        if (_downloads.ContainsKey(player.UserId))
        {
            _mapping.Notify(player, false, "wf-mapping-mode-download-busy");
            return;
        }

        try
        {
            if (!_library.TryFindVersion(player.UserId, ev.MapName, ev.Version, out var map, out var version))
            {
                _mapping.Notify(player, false, "wf-mapping-mode-library-not-found");
                return;
            }

            if (!TryGetFiles(map.Name, version, ev.Ship, out var files, out var error, out var errorArgs))
            {
                _mapping.Notify(player, false, error, errorArgs);
                return;
            }

            var download = new Download(player, map.Name, version.Name.Folder,
                files.Select(file => new DownloadFile(file.Name, file.Path, null)).ToList())
            {
                LastActivity = _timing.RealTime,
            };

            _downloads[player.UserId] = download;
            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{player:player} downloads their library map \"{map.Name}\" {version.Name.Folder} ({files.Count} files{(ev.Ship ? ", as a ship" : string.Empty)})");
            NextFile(download);
        }
        catch (Exception e)
        {
            Log.Error($"Starting a mapping library download for {player} failed: {e}");
            if (_downloads.ContainsKey(player.UserId))
                Cancel(player.UserId, "wf-mapping-mode-download-failed");
            else
                _mapping.Notify(player, false, "wf-mapping-mode-download-failed");
        }
    }

    /// <summary>Whether a player has a download running.</summary>
    public bool IsDownloading(NetUserId user)
    {
        return _downloads.ContainsKey(user);
    }

    /// <summary>
    /// Starts sending one in-memory file, such as an export package, as a player's download; false, telling them why,
    /// when they already have one running or the data is empty or over <see cref="WFMappingDownloadAssembler.MaxFileBytes"/>.
    /// </summary>
    public bool TryStartData(ICommonSession player, string mapName, string version, string fileName, byte[] data,
        string logNote)
    {
        if (_downloads.ContainsKey(player.UserId))
        {
            _mapping.Notify(player, false, "wf-mapping-mode-download-busy");
            return false;
        }

        if (data.Length == 0)
        {
            _mapping.Notify(player, false, "wf-mapping-mode-download-empty", ("file", fileName));
            return false;
        }

        if (data.Length > WFMappingDownloadAssembler.MaxFileBytes)
        {
            _mapping.Notify(player, false, "wf-mapping-mode-download-too-large",
                ("max", WFMappingDownloadAssembler.MaxFileBytes / (1024 * 1024)));
            return false;
        }

        var download = new Download(player, mapName, version, new List<DownloadFile> { new(fileName, null, data) })
        {
            LastActivity = _timing.RealTime,
        };

        _downloads[player.UserId] = download;
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} downloads {fileName} of their library map \"{mapName}\" {version} ({logNote}, {data.Length} bytes)");
        try
        {
            NextFile(download);
        }
        catch (Exception e)
        {
            Log.Error($"Starting a mapping export download for {player} failed: {e}");
            Cancel(player.UserId, "wf-mapping-mode-download-failed");
            return false;
        }

        return true;
    }

    /// <summary>
    /// The files a download sends and the names they arrive under. A single level is <c>&lt;MapName&gt;.yml</c>, its
    /// grid file when it has one; several levels are <c>&lt;MapName&gt;_z&lt;D&gt;.yml</c>. A ship is the one grid as
    /// <c>&lt;MapName&gt;.yml</c> and its vessel prototype as <c>&lt;MapName&gt;.vessel.yml</c>, refused unless the
    /// version holds exactly one grid with its grid file.
    /// </summary>
    public static bool TryGetFiles(string mapName, WFMappingLibrarySystem.LibraryVersion version, bool ship,
        out List<(string Name, ResPath Path)> files, out string error, out (string, object)[] args)
    {
        files = new List<(string Name, ResPath Path)>();
        error = string.Empty;
        args = Array.Empty<(string, object)>();

        if (!ship)
        {
            if (version.Levels.Count == 1)
            {
                var (depth, level) = version.Levels.First();
                files.Add((WFMappingLibraryNames.DownloadMapFile(mapName), version.Grids.GetValueOrDefault(depth, level)));
                return true;
            }

            foreach (var (depth, level) in version.Levels)
            {
                files.Add((WFMappingLibraryNames.DownloadLevelFile(mapName, depth), level));
            }

            return true;
        }

        var total = version.TotalGrids;
        if (total < 0)
        {
            error = "wf-mapping-mode-library-read-failed";
            return false;
        }

        if (total == 0)
        {
            error = "wf-mapping-ship-no-grid";
            return false;
        }

        if (total > 1)
        {
            error = "wf-mapping-ship-several-grids";
            args = new (string, object)[] { ("count", total) };
            return false;
        }

        if (version.ShipDepth is not { } shipDepth)
        {
            error = "wf-mapping-ship-no-grid-file";
            return false;
        }

        files.Add((WFMappingLibraryNames.DownloadMapFile(mapName), version.Grids[shipDepth]));
        if (version.Vessels.TryGetValue(shipDepth, out var vessel))
            files.Add((WFMappingLibraryNames.DownloadVesselFile(mapName), vessel));

        return true;
    }

    /// <summary>Reads the next file and starts sending it, or finishes the download after the last.</summary>
    private void NextFile(Download download)
    {
        var user = download.Player.UserId;
        download.FileIndex++;
        if (download.FileIndex >= download.Files.Count)
        {
            _downloads.Remove(user);
            _mapping.Notify(download.Player, true, "wf-mapping-mode-download-done", ("name", download.MapName),
                ("version", download.Version), ("count", download.Files.Count));
            return;
        }

        var (name, path, bytes) = download.Files[download.FileIndex];
        var data = bytes;
        if (data == null && path is { } file)
        {
            using var stream = _res.UserData.OpenRead(file);
            data = ReadCapped(stream, WFMappingDownloadAssembler.MaxFileBytes);
        }

        if (data == null)
        {
            Cancel(user, "wf-mapping-mode-download-too-large",
                ("max", WFMappingDownloadAssembler.MaxFileBytes / (1024 * 1024)));
            return;
        }

        if (data.Length == 0)
        {
            Log.Warning($"Library file {path} of \"{download.MapName}\" {download.Version} is empty; not sending it to {download.Player}");
            Cancel(user, "wf-mapping-mode-download-empty", ("file", name));
            return;
        }

        download.Data = data;
        download.TransferId = _nextTransferId++;
        download.ChunkCount = WFMappingDownloadAssembler.ChunksFor(data.Length);
        download.NextChunk = 0;
        download.Acked = 0;
        Log.Debug($"Sending {name} of \"{download.MapName}\" {download.Version} to {download.Player} in {download.ChunkCount} chunks");
        Pump(download);
    }

    /// <summary>
    /// Reads a stream to its end, or null past <paramref name="cap"/> bytes. The stream's <c>Length</c> isn't trusted:
    /// the in-memory data folder of tests reports wherever it was last read up to, and a file system may report 0.
    /// </summary>
    public static byte[]? ReadCapped(Stream stream, int cap)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > cap)
                return null;

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    /// <summary>Sends chunks until <see cref="InFlight"/> are unacknowledged or the file is all sent.</summary>
    private void Pump(Download download)
    {
        var name = download.Files[download.FileIndex].Name;
        while (download.NextChunk < download.ChunkCount && download.NextChunk - download.Acked < InFlight)
        {
            var offset = download.NextChunk * WFMappingDownloadAssembler.ChunkSize;
            var length = Math.Min(WFMappingDownloadAssembler.ChunkSize, download.Data.Length - offset);
            var data = download.Data.AsSpan(offset, length).ToArray();

            RaiseNetworkEvent(new WFMappingDownloadChunkEvent(download.TransferId, download.MapName, download.Version,
                name, download.FileIndex, download.Files.Count, download.NextChunk, download.ChunkCount,
                download.Data.Length, data),
                download.Player);
            download.NextChunk++;
        }

        download.LastActivity = _timing.RealTime;
    }

    private void OnAck(WFMappingDownloadAckEvent ev, EntitySessionEventArgs args)
    {
        var user = args.SenderSession.UserId;
        if (!_downloads.TryGetValue(user, out var download) ||
            ev.TransferId != download.TransferId ||
            ev.Chunk != download.Acked ||
            ev.Chunk >= download.NextChunk)
        {
            return;
        }

        try
        {
            download.Acked++;
            if (download.Acked >= download.ChunkCount)
                NextFile(download);
            else
                Pump(download);
        }
        catch (Exception e)
        {
            Log.Error($"Mapping library download for {args.SenderSession} failed: {e}");
            Cancel(user, "wf-mapping-mode-download-failed");
        }
    }

    private void OnCancelRequest(WFMappingDownloadCancelRequest ev, EntitySessionEventArgs args)
    {
        Cancel(args.SenderSession.UserId, "wf-mapping-mode-download-cancelled");
    }

    private void OnMembershipEnded(WFMappingMembershipEndedEvent ev)
    {
        Cancel(ev.User, "wf-mapping-mode-download-cancelled");
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        _downloads.Remove(args.Session.UserId);
        _requestThrottle.Forget(args.Session.UserId);
    }

    /// <summary>Drops a player's download, tells their client to discard it, and tells them why.</summary>
    private void Cancel(NetUserId user, string reason, params (string, object)[] args)
    {
        if (!_downloads.Remove(user, out var download))
            return;

        if (download.Player.Status == SessionStatus.Disconnected)
            return;

        RaiseNetworkEvent(new WFMappingDownloadCancelledEvent(download.TransferId), download.Player);
        _mapping.Notify(download.Player, false, reason, args);
    }
}
