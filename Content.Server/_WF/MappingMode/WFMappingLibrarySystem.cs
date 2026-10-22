using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Content.Server._WF.MappingTools;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Administration.Logs;
using Content.Shared._WF.MappingMode;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// The per-player map library in the server's data folder: versioned manual saves and autosaves of sessions,
/// opening a version as a new session, and deleting maps and versions. The listing comes from folder names.
/// </summary>
public sealed class WFMappingLibrarySystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MappingZLevelsSystem _zLevels = default!;
    [Dependency] private WFMappingModeSystem _mapping = default!;
    [Dependency] private WFMappingVesselSystem _vessel = default!;
    [Dependency] private WFMappingTemplateShareSystem _share = default!;

    /// <summary>
    /// A version folder on disk: its name, level files by depth, the grid and vessel files beside levels holding one
    /// grid, each level's grid count from its file header (-1 when unreadable) and size.
    /// </summary>
    public sealed class LibraryVersion(WFMappingVersionName name)
    {
        public readonly WFMappingVersionName Name = name;
        public readonly SortedDictionary<int, ResPath> Levels = new();
        public readonly SortedDictionary<int, ResPath> Grids = new();
        public readonly SortedDictionary<int, ResPath> Vessels = new();
        public readonly SortedDictionary<int, int> GridCounts = new();
        public long Bytes;

        /// <summary>The shared template saved with the version, and its placement.</summary>
        public ResPath? TemplateImage;
        public ResPath? TemplateSettings;

        /// <summary>Grids across every level, or -1 when a level's count couldn't be read.</summary>
        public int TotalGrids => GridCounts.Values.Any(c => c < 0) ? -1 : GridCounts.Values.Sum();

        /// <summary>The depth of the one grid file, when the whole version holds exactly that one grid.</summary>
        public int? ShipDepth => TotalGrids == 1 && Grids.Count == 1 && GridCounts.GetValueOrDefault(Grids.Keys.First()) == 1
            ? Grids.Keys.First()
            : null;
    }

    /// <summary>A map folder on disk and its versions, oldest first.</summary>
    public sealed class LibraryMap(string name)
    {
        public readonly string Name = name;
        public readonly List<LibraryVersion> Versions = new();
        public long Bytes;
    }

    /// <summary>A player's library as last read from disk.</summary>
    private sealed class LibraryScan
    {
        public readonly List<LibraryMap> Maps = new();
        public long Bytes;
        public TimeSpan At;

        /// <summary>Maps with at least one version, which is what <c>max_maps</c> counts.</summary>
        public int MapCount => Maps.Count(m => m.Versions.Count > 0);
    }

    private readonly Dictionary<NetUserId, LibraryScan> _scans = new();

    /// <summary>How long a scan is trusted; saves, prunes and deletes made here drop it at once.</summary>
    private static readonly TimeSpan ScanLifetime = TimeSpan.FromSeconds(30);

    private readonly WFMappingThrottle<NetUserId> _saveThrottle = new(1 / 10.0, 1);
    private readonly WFMappingThrottle<NetUserId> _listThrottle = new(2, 2);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFMappingLibraryRequest>(OnLibraryRequest);
        SubscribeNetworkEvent<WFMappingSaveRequest>(OnSaveRequest);
        SubscribeNetworkEvent<WFMappingOpenRequest>(OnOpenRequest);
        SubscribeNetworkEvent<WFMappingDeleteRequest>(OnDeleteRequest);
    }

    #region Scanning

    /// <summary>Reads a player's library from disk, or the recent scan of it.</summary>
    private LibraryScan GetScan(NetUserId user, bool fresh = false)
    {
        var now = _timing.RealTime;
        if (!fresh && _scans.TryGetValue(user, out var cached) && now - cached.At < ScanLifetime)
            return cached;

        var scan = new LibraryScan { At = now };
        var root = WFMappingLibraryNames.UserFolder(user);
        if (_res.UserData.IsDir(root))
        {
            foreach (var entry in _res.UserData.DirectoryEntries(root).ToList())
            {
                var path = root / entry;
                if (!_res.UserData.IsDir(path) || !WFMappingLibraryNames.IsValidMapName(entry))
                    continue;

                var map = ScanMap(path, entry);
                scan.Maps.Add(map);
                scan.Bytes += map.Bytes;
            }
        }

        scan.Maps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        _scans[user] = scan;
        return scan;
    }

    private LibraryMap ScanMap(ResPath folder, string name)
    {
        var map = new LibraryMap(name);
        foreach (var entry in _res.UserData.DirectoryEntries(folder).ToList())
        {
            var path = folder / entry;
            if (!_res.UserData.IsDir(path) || !WFMappingLibraryNames.TryParseVersion(entry, out var versionName))
                continue;

            var version = new LibraryVersion(versionName);
            foreach (var file in _res.UserData.DirectoryEntries(path).ToList())
            {
                var filePath = path / file;
                if (_res.UserData.IsDir(filePath))
                    continue;

                if (WFMappingLibraryNames.TryParseLevelFile(file, out var depth))
                {
                    version.Bytes += ReadLevelHeader(filePath, out var grids);
                    version.Levels[depth] = filePath;
                    version.GridCounts[depth] = grids;
                    continue;
                }

                version.Bytes += FileSize(filePath);
                if (WFMappingLibraryNames.TryParseGridFile(file, out depth))
                    version.Grids[depth] = filePath;
                else if (WFMappingLibraryNames.TryParseVesselFile(file, out depth))
                    version.Vessels[depth] = filePath;
                else if (file == WFMappingTemplateShareSystem.ImageFile)
                    version.TemplateImage = filePath;
                else if (file == WFMappingTemplateShareSystem.SettingsFile)
                    version.TemplateSettings = filePath;
            }

            map.Versions.Add(version);
            map.Bytes += version.Bytes;
        }

        map.Versions.Sort((a, b) => a.Name.Number.CompareTo(b.Name.Number));
        return map;
    }

    private long FileSize(ResPath path)
    {
        using var stream = _res.UserData.OpenRead(path);
        return stream.Length;
    }

    /// <summary>Lines of a level file's header read for its grid list; the list comes long before the entities.</summary>
    private const int MaxHeaderLines = 4096;

    /// <summary>
    /// A level file's size, and how many grids its <c>grids:</c> header lists (-1 when the header has none), read
    /// without parsing the file.
    /// </summary>
    private long ReadLevelHeader(ResPath path, out int grids)
    {
        using var stream = _res.UserData.OpenRead(path);
        var bytes = stream.Length;
        grids = -1;
        try
        {
            grids = CountHeaderGrids(stream);
        }
        finally
        {
            // The in-memory data folder of tests reports a file's length as wherever it was last read up to.
            stream.Position = bytes;
            stream.ReadByte();
        }

        return bytes;
    }

    /// <summary>The length of the <c>grids:</c> list at the head of a map file, or -1 without one.</summary>
    private static int CountHeaderGrids(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var inList = false;
        var count = 0;
        for (var i = 0; i < MaxHeaderLines && reader.ReadLine() is { } line; i++)
        {
            if (inList)
            {
                if (!line.StartsWith("- ", StringComparison.Ordinal))
                    return count;

                count++;
                continue;
            }

            if (line.StartsWith("grids:", StringComparison.Ordinal))
            {
                if (line.TrimEnd() == "grids: []")
                    return 0;

                inList = true;
            }
            else if (line.StartsWith("entities:", StringComparison.Ordinal))
            {
                break;
            }
        }

        return inList ? count : -1;
    }

    /// <summary>The map whose folder matches a name, ignoring case as Windows does.</summary>
    private static LibraryMap? FindMap(LibraryScan scan, string name)
    {
        return scan.Maps.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A version in a player's library: <paramref name="version"/> names its folder, or null for the newest. Only
    /// versions with a ground level count.
    /// </summary>
    public bool TryFindVersion(NetUserId user, string mapName, string? version,
        [NotNullWhen(true)] out LibraryMap? map, [NotNullWhen(true)] out LibraryVersion? found)
    {
        found = null;
        map = null;
        if (!WFMappingLibraryNames.TryNormalizeMapName(mapName, out var name) ||
            version is { Length: > WFMappingModeLimits.MaxNameLength })
        {
            return false;
        }

        map = FindMap(GetScan(user, fresh: true), name);
        if (map == null)
            return false;

        found = version == null
            ? map.Versions.LastOrDefault(v => v.Levels.ContainsKey(0))
            : map.Versions.FirstOrDefault(v => v.Name.Folder == version && v.Levels.ContainsKey(0));
        return found != null;
    }

    #endregion

    #region Listing

    private void OnLibraryRequest(WFMappingLibraryRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_listThrottle.TryTake(player.UserId, _timing.CurTime))
            return;

        SendLibrary(player);
    }

    /// <summary>Sends a player their library.</summary>
    public void SendLibrary(ICommonSession player)
    {
        LibraryScan scan;
        try
        {
            scan = GetScan(player.UserId);
        }
        catch (Exception e)
        {
            Log.Error($"Reading the mapping library of {player} failed: {e}");
            _mapping.Notify(player, false, "wf-mapping-mode-library-read-failed");
            return;
        }

        var maps = new List<WFMappingLibraryMap>();
        foreach (var map in scan.Maps)
        {
            if (map.Versions.Count == 0)
                continue;

            var versions = new List<WFMappingLibraryVersion>(map.Versions.Count);
            for (var i = map.Versions.Count - 1; i >= 0; i--)
            {
                var version = map.Versions[i];
                versions.Add(new WFMappingLibraryVersion(version.Name, version.Levels.Count, version.Bytes,
                    version.ShipDepth != null));
            }

            maps.Add(new WFMappingLibraryMap(map.Name, versions));
        }

        Log.Debug($"Sending {player} their mapping library: {maps.Count} maps, {scan.Bytes} bytes");
        RaiseNetworkEvent(new WFMappingLibraryEvent(maps, scan.Bytes,
            _cfg.GetCVar(WFMappingModeCVars.MaxLibraryBytes),
            _cfg.GetCVar(WFMappingModeCVars.MaxMaps),
            _cfg.GetCVar(WFMappingModeCVars.MaxVersions)), player);
    }

    private void SendLibrary(NetUserId user)
    {
        if (_players.TryGetSessionById(user, out var player))
            SendLibrary(player);
    }

    #endregion

    #region Saving

    /// <summary>The outcome of a save: the version folder written, or a Fluent id and its arguments.</summary>
    private readonly record struct SaveResult(string? Version, string Error, (string, object)[] Args)
    {
        public static SaveResult Fail(string error, params (string, object)[] args) => new(null, error, args);
    }

    private void OnSaveRequest(WFMappingSaveRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_mapping.TryGetPresent(player, out var session))
            return;

        if (!_saveThrottle.TryTake(player.UserId, _timing.CurTime))
        {
            _mapping.Notify(player, false, "wf-mapping-mode-save-too-soon");
            return;
        }

        var name = session.Name;
        if (!string.IsNullOrWhiteSpace(ev.Name) && !WFMappingLibraryNames.TryNormalizeMapName(ev.Name, out name))
        {
            _mapping.Notify(player, false, "wf-mapping-mode-bad-name", ("max", WFMappingModeLimits.MaxNameLength));
            return;
        }

        var result = SaveVersion(session, player.UserId, name, WFMappingVersionKind.Manual);
        if (result.Version == null)
        {
            _mapping.Notify(player, false, result.Error, result.Args);
            return;
        }

        // Only the owner's save stands in for the autosave, which goes to the owner's library.
        session.LastSaved = DateTime.UtcNow;
        if (session.Owner == player.UserId)
            session.Dirty = false;

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} saved mapping session {session.Code} to their library as \"{name}\" {result.Version}");
        _mapping.Notify(player, true, "wf-mapping-mode-saved", ("name", name), ("version", result.Version));
        _mapping.SendState(session);
        SendLibrary(player);
    }

    /// <summary>
    /// Saves every dirty session into its owner's library, before a round restart deletes the maps.
    /// </summary>
    public void AutosaveAll()
    {
        foreach (var session in _mapping.Sessions.ToList())
        {
            if (session.Dirty)
                Autosave(session);
        }
    }

    /// <summary>
    /// Saves one session into its owner's library as an autosave under the session's name; true when it was written.
    /// The owner hears only of failures.
    /// </summary>
    public bool Autosave(WFMappingSession session)
    {
        var owner = session.Owner;
        var name = WFMappingLibraryNames.IsValidMapName(session.Name) ? session.Name : session.Code;
        var result = SaveVersion(session, owner, name, WFMappingVersionKind.Auto);
        if (result.Version == null)
        {
            Log.Warning($"Autosave of mapping session {session.Code} into {owner}'s library failed: {result.Error}");
            if (_players.TryGetSessionById(owner, out var player))
            {
                _mapping.Notify(player, false, "wf-mapping-mode-autosave-failed", ("name", name));
                _mapping.Notify(player, false, result.Error, result.Args);
            }

            return false;
        }

        session.Dirty = false;
        session.LastSaved = DateTime.UtcNow;
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Mapping session {session.Code} autosaved to {owner}'s library as \"{name}\" {result.Version}");
        SendLibrary(owner);
        return true;
    }

    /// <summary>Writes a session's levels as the next version of a map in a player's library.</summary>
    private SaveResult SaveVersion(WFMappingSession session, NetUserId user, string mapName, WFMappingVersionKind kind)
    {
        List<EntityUid> levels;
        int ground;
        try
        {
            if (TerminatingOrDeleted(session.RootMap))
                return SaveResult.Fail("wf-mapping-mode-save-failed");

            levels = _zLevels.GetLevels(session.RootMap, out ground);
            if (levels.Any(level => !_mapping.IsSessionMap(level, session)))
            {
                Log.Error($"Mapping session {session.Code} has a level outside the session; not saving it");
                return SaveResult.Fail("wf-mapping-mode-save-failed");
            }
        }
        catch (Exception e)
        {
            Log.Error($"Reading the levels of mapping session {session.Code} failed: {e}");
            return SaveResult.Fail("wf-mapping-mode-save-failed");
        }

        FixAtmos(levels, session);
        return SaveVersion(levels, ground, user, mapName, kind, $"mapping session {session.Code}", session.Template);
    }

    /// <summary>Resets the air on every grid of the levels to its roundstart mix, as <c>fixgridatmos</c> does.</summary>
    private void FixAtmos(List<EntityUid> levels, WFMappingSession session)
    {
        foreach (var level in levels)
        {
            var children = Transform(level).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (!HasComp<MapGridComponent>(child))
                    continue;

                try
                {
                    _atmos.WfFixGridAtmos(child);
                }
                catch (Exception e)
                {
                    Log.Warning($"Fixing the atmosphere of {ToPrettyString(child)} in mapping session {session.Code} failed: {e}");
                }
            }
        }
    }

    /// <summary>
    /// Stores an uploaded map, loaded aside and checked, as the next <see cref="WFMappingVersionKind.Upload"/>
    /// version of a map in a player's library; only the engine's save of <paramref name="map"/> is written.
    /// </summary>
    public bool SaveUpload(NetUserId user, string mapName, EntityUid map, [NotNullWhen(true)] out string? version,
        out string error, out (string, object)[] args)
    {
        var result = SaveVersion(new List<EntityUid> { map }, 0, user, mapName, WFMappingVersionKind.Upload,
            $"an upload of {user}");
        version = result.Version;
        error = result.Error;
        args = result.Args;
        return version != null;
    }

    /// <summary>
    /// Whether a player's library takes a version of <paramref name="mapName"/>: an existing map always does, a new
    /// one only below <c>max_maps</c>.
    /// </summary>
    public bool HasRoomForMap(NetUserId user, string mapName, out int maxMaps)
    {
        maxMaps = _cfg.GetCVar(WFMappingModeCVars.MaxMaps);
        var scan = GetScan(user);
        return FindMap(scan, mapName) is { Versions.Count: > 0 } || scan.MapCount < maxMaps;
    }

    /// <summary>
    /// Writes levels, listed bottom-up with depth 0 at <paramref name="ground"/>, and the session's shared template
    /// if any, as the next version of a map in a player's library, then prunes the map to <c>max_versions</c>.
    /// Refused at <c>max_maps</c>, or when the library would end up over <c>max_library_bytes</c>; a refused or failed
    /// save leaves nothing behind. <paramref name="logName"/> names what was saved in the log.
    /// </summary>
    private SaveResult SaveVersion(List<EntityUid> levels, int ground, NetUserId user, string mapName,
        WFMappingVersionKind kind, string logName, WFMappingSessionTemplate? template = null)
    {
        ResPath? written = null;
        try
        {
            var scan = GetScan(user, fresh: true);
            var map = FindMap(scan, mapName);
            var maxMaps = _cfg.GetCVar(WFMappingModeCVars.MaxMaps);
            if ((map == null || map.Versions.Count == 0) && scan.MapCount >= maxMaps)
                return SaveResult.Fail("wf-mapping-mode-library-full-maps", ("max", maxMaps));

            var maxBytes = _cfg.GetCVar(WFMappingModeCVars.MaxLibraryBytes);
            if (scan.Bytes >= maxBytes)
                return SaveResult.Fail("wf-mapping-mode-library-full-bytes", ("max", maxBytes / (1024 * 1024)));

            var folderName = map?.Name ?? mapName;
            var number = map is { Versions.Count: > 0 } ? map.Versions.Max(v => v.Name.Number) + 1 : 1;
            var now = DateTime.UtcNow;
            var stamp = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            var versionName = new WFMappingVersionName(number, stamp, kind);
            var folder = WFMappingLibraryNames.VersionFolder(user, folderName, versionName.Folder);
            if (_res.UserData.Exists(folder))
                return SaveResult.Fail("wf-mapping-mode-save-failed");

            written = folder;
            if (!_zLevels.TrySaveLevels(levels, ground,
                    depth => WFMappingLibraryNames.LevelPath(user, folderName, versionName.Folder, depth)) ||
                !SaveGrids(levels, ground, user, folderName, versionName.Folder))
            {
                DeleteQuietly(folder);
                return SaveResult.Fail("wf-mapping-mode-save-failed");
            }

            if (template != null)
                _share.WriteSaved(template, folder);

            var saved = ScanMap(WFMappingLibraryNames.MapFolder(user, folderName), folderName);
            var newBytes = saved.Versions.FirstOrDefault(v => v.Name == versionName)?.Bytes ?? 0;
            var prune = WFMappingPrune.SelectForRemoval(saved.Versions.Select(v => v.Name).ToList(),
                _cfg.GetCVar(WFMappingModeCVars.MaxVersions));
            var freed = saved.Versions.Where(v => prune.Contains(v.Name)).Sum(v => v.Bytes);
            if (scan.Bytes + newBytes - freed > maxBytes)
            {
                DeleteQuietly(folder);
                return SaveResult.Fail("wf-mapping-mode-library-full-bytes", ("max", maxBytes / (1024 * 1024)));
            }

            written = null;
            foreach (var old in prune)
            {
                DeleteQuietly(WFMappingLibraryNames.VersionFolder(user, folderName, old.Folder));
            }

            if (prune.Count > 0)
                Log.Info($"Pruned {prune.Count} versions of {user}'s library map \"{folderName}\"");

            return new SaveResult(versionName.Folder, string.Empty, Array.Empty<(string, object)>());
        }
        catch (Exception e)
        {
            Log.Error($"Saving {logName} into {user}'s library as \"{mapName}\" failed: {e}");
            if (written is { } partial)
                DeleteQuietly(partial);

            return SaveResult.Fail("wf-mapping-mode-save-failed");
        }
        finally
        {
            _scans.Remove(user);
        }
    }

    /// <summary>
    /// Saves the grid of every level that holds exactly one as a grid file beside the level, with its generated vessel
    /// prototype; false when a grid file failed. A vessel that fails to generate is only logged.
    /// </summary>
    private bool SaveGrids(List<EntityUid> levels, int ground, NetUserId user, string mapName, string version)
    {
        for (var i = 0; i < levels.Count; i++)
        {
            if (SingleGrid(levels[i]) is not { } grid)
                continue;

            var depth = i - ground;
            if (!_loader.TrySaveGrid(grid, WFMappingLibraryNames.GridPath(user, mapName, version, depth)))
                return false;

            try
            {
                _res.UserData.WriteAllText(WFMappingLibraryNames.VesselPath(user, mapName, version, depth),
                    _vessel.Generate(grid, mapName));
            }
            catch (Exception e)
            {
                Log.Warning($"Generating the vessel prototype of {ToPrettyString(grid)} for \"{mapName}\" failed: {e}");
            }
        }

        return true;
    }

    /// <summary>The map's only grid, or null when it has none or several.</summary>
    private EntityUid? SingleGrid(EntityUid map)
    {
        EntityUid? found = null;
        var children = Transform(map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!HasComp<MapGridComponent>(child))
                continue;

            if (found != null)
                return null;

            found = child;
        }

        return found;
    }

    private void DeleteQuietly(ResPath path)
    {
        try
        {
            _res.UserData.Delete(path);
        }
        catch (Exception e)
        {
            Log.Error($"Couldn't delete {path} from a mapping library: {e}");
        }
    }

    #endregion

    #region Opening and deleting

    private void OnOpenRequest(WFMappingOpenRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.CanStartRequest(player) || !_mapping.CheckSessionCap(player))
            return;

        LibraryMap? map;
        LibraryVersion? version;
        try
        {
            if (!TryFindVersion(player.UserId, ev.MapName, ev.Version, out map, out version))
            {
                _mapping.Notify(player, false, "wf-mapping-mode-library-not-found");
                return;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Reading the mapping library of {player} failed: {e}");
            _mapping.Notify(player, false, "wf-mapping-mode-library-read-failed");
            return;
        }

        List<EntityUid>? levels = null;
        try
        {
            if (!_zLevels.TryLoadLevels(version.Levels, out levels, out var ground))
            {
                _mapping.Notify(player, false, "wf-mapping-mode-open-failed", ("name", map.Name));
                return;
            }

            WFMappingSessionTemplate? template = null;
            if (version.TemplateImage is { } image)
                template = _share.ReadSaved(image, version.TemplateSettings, player, -ground, levels.Count - 1 - ground);

            if (_mapping.TryOpenSession(player, map.Name, levels, ground, map.Name, version.Name.Folder, out _,
                    template))
            {
                return;
            }

            DeleteLevels(levels);
        }
        catch (Exception e)
        {
            Log.Error($"Opening {player}'s library map \"{map.Name}\" {version.Name.Folder} failed: {e}");
            if (levels != null)
                DeleteLevels(levels);

            _mapping.Notify(player, false, "wf-mapping-mode-open-failed", ("name", map.Name));
        }
    }

    /// <summary>Deletes loaded levels that no session took, unless a session did after all.</summary>
    private void DeleteLevels(List<EntityUid> levels)
    {
        _mapping.DeleteMaps(levels.Where(level => !_mapping.TryGetSessionOfMap(level, out _)));
    }

    private void OnDeleteRequest(WFMappingDeleteRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!_mapping.Enabled || !_listThrottle.TryTake(player.UserId, _timing.CurTime))
            return;

        var user = player.UserId;
        try
        {
            if (!WFMappingLibraryNames.TryNormalizeMapName(ev.MapName, out var name) ||
                FindMap(GetScan(user, fresh: true), name) is not { } map)
            {
                _mapping.Notify(player, false, "wf-mapping-mode-library-not-found");
                return;
            }

            if (ev.Version == null)
            {
                _res.UserData.Delete(WFMappingLibraryNames.MapFolder(user, map.Name));
                _adminLog.Add(LogType.Action, LogImpact.Low,
                    $"{player:player} deleted their library map \"{map.Name}\" ({map.Versions.Count} versions)");
                _mapping.Notify(player, true, "wf-mapping-mode-deleted-map", ("name", map.Name));
            }
            else
            {
                var version = map.Versions.FirstOrDefault(v => v.Name.Folder == ev.Version);
                if (version == null)
                {
                    _mapping.Notify(player, false, "wf-mapping-mode-library-not-found");
                    return;
                }

                // The last version takes its map with it.
                _res.UserData.Delete(map.Versions.Count == 1
                    ? WFMappingLibraryNames.MapFolder(user, map.Name)
                    : WFMappingLibraryNames.VersionFolder(user, map.Name, version.Name.Folder));
                _adminLog.Add(LogType.Action, LogImpact.Low,
                    $"{player:player} deleted {version.Name.Folder} of their library map \"{map.Name}\"");
                _mapping.Notify(player, true, "wf-mapping-mode-deleted-version", ("name", map.Name),
                    ("version", version.Name.Folder));
            }
        }
        catch (Exception e)
        {
            Log.Error($"Deleting from the mapping library of {player} failed: {e}");
            _mapping.Notify(player, false, "wf-mapping-mode-delete-failed");
        }
        finally
        {
            _scans.Remove(user);
        }

        SendLibrary(player);
    }

    #endregion
}
