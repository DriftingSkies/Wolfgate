using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._CE.ZLevels.Core;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Popups;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WF.MappingTools;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingTools;

/// <summary>
/// Mapping z-stacks: moves mappers between levels, adds and removes levels, and saves and opens a stack as one map
/// file per level. Only stacks whose maps are all still uninitialized can be changed, so live stations and planets
/// are left alone.
/// </summary>
public sealed class MappingZLevelsSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private CEZLevelsSystem _zLevels = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MappingMapsSystem _maps = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private TransformSystem _transform = default!;

    private static readonly Regex SaveName = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MappingZLevelsMoveEvent>(OnMove);
        SubscribeNetworkEvent<MappingZLevelsAddEvent>(OnAdd);
        SubscribeNetworkEvent<MappingZLevelsRemoveEvent>(OnRemove);
        SubscribeNetworkEvent<MappingZLevelsSaveEvent>(OnSave);
        SubscribeNetworkEvent<MappingZLevelsOpenEvent>(OnOpen);
    }

    private bool CanUse(ICommonSession session)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Mapping);
    }

    /// <summary>
    /// The levels of the stack a map belongs to, bottom-up, and the index of the one on depth 0. A map outside any
    /// stack is a stack of one.
    /// </summary>
    public List<EntityUid> GetLevels(EntityUid map, out int ground)
    {
        ground = 0;
        if (!_zLevels.TryGetMapNetwork(map, out var network))
            return new List<EntityUid> { map };

        var levels = new List<EntityUid>();
        foreach (var (depth, level) in network.Comp.ZLevels.OrderBy(pair => pair.Key))
        {
            if (level is not { } uid || TerminatingOrDeleted(uid))
                continue;

            if (depth == 0)
                ground = levels.Count;

            levels.Add(uid);
        }

        return levels;
    }

    /// <summary>
    /// Whether every level is a map still being mapped, which is what the mapping command and stack opening make.
    /// </summary>
    private bool IsEditable(List<EntityUid> levels)
    {
        return levels.All(level => !_map.IsInitialized(level));
    }

    private bool TryGetPlayerMap(ICommonSession session, out EntityUid player, out EntityUid map)
    {
        map = default;
        player = session.AttachedEntity ?? default;
        if (!player.IsValid() || Transform(player).MapUid is not { } mapUid)
            return false;

        map = mapUid;
        return true;
    }

    private void OnMove(MappingZLevelsMoveEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!CanUse(session) || !TryGetPlayerMap(session, out var player, out var map))
            return;

        if (!_zLevels.TryMapOffset(map, ev.Offset, out var target, out var targetMap))
        {
            _popup.PopupCursor(Loc.GetString(ev.Offset > 0 ? "wf-mapping-zlevels-none-above" : "wf-mapping-zlevels-none-below"), session);
            return;
        }

        MoveTo(player, targetMap.MapId);
        _popup.PopupCursor(Loc.GetString("wf-mapping-zlevels-moved", ("depth", target.Comp.Depth)), session);
    }

    private void MoveTo(EntityUid uid, MapId map)
    {
        _transform.SetMapCoordinates(uid, new MapCoordinates(_transform.GetWorldPosition(uid), map));
    }

    private void OnAdd(MappingZLevelsAddEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!CanUse(session) || !TryGetPlayerMap(session, out _, out var map))
            return;

        if (!TryAddLevel(map, ev.Above, out var added, out var depth, out var error))
        {
            _popup.PopupCursor(Loc.GetString(error), session);
            return;
        }

        _popup.PopupCursor(Loc.GetString("wf-mapping-zlevels-added", ("depth", depth)), session);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{session:player} added z-level {ToPrettyString(added)} {(ev.Above ? "above" : "below")} {ToPrettyString(map)}");
    }

    /// <summary>
    /// Adds an empty map directly above or below <paramref name="map"/> in its stack, making one if it has none. The
    /// ground level keeps depth 0, so the levels on the far side of the new one shift away from it.
    /// </summary>
    public bool TryAddLevel(EntityUid map, bool above, out EntityUid added, out int depth, [NotNullWhen(false)] out string? error)
    {
        added = default;
        depth = 0;
        error = null;

        var levels = GetLevels(map, out var ground);
        if (!IsEditable(levels))
        {
            error = "wf-mapping-zlevels-live";
            return false;
        }

        var groundMap = levels[ground];
        var index = levels.IndexOf(map) + (above ? 1 : 0);
        added = _map.CreateMap(out _, runMapInit: false);
        levels.Insert(index, added);

        ground = levels.IndexOf(groundMap);
        _zLevels.WfRestack(levels, ground);
        depth = index - ground;
        return true;
    }

    private void OnRemove(MappingZLevelsRemoveEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!CanUse(session) || !TryGetPlayerMap(session, out _, out var map))
            return;

        if (!TryRemoveLevel(map, out var error))
        {
            _popup.PopupCursor(Loc.GetString(error), session);
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{session:player} removed z-level {ToPrettyString(map)}");
        _popup.PopupCursor(Loc.GetString("wf-mapping-zlevels-removed"), session);
    }

    /// <summary>
    /// Deletes <paramref name="map"/> from its stack and closes the gap. Removing the ground level makes the one
    /// above it, or else the one below, the new ground; players on the removed level land on whichever level now
    /// has its place.
    /// </summary>
    public bool TryRemoveLevel(EntityUid map, [NotNullWhen(false)] out string? error)
    {
        error = null;

        var levels = GetLevels(map, out var ground);
        if (levels.Count < 2)
        {
            error = "wf-mapping-zlevels-last";
            return false;
        }

        if (!IsEditable(levels))
        {
            error = "wf-mapping-zlevels-live";
            return false;
        }

        var index = levels.IndexOf(map);
        levels.RemoveAt(index);

        if (index < ground || index == ground && index == levels.Count)
            ground--;

        var refuge = Comp<MapComponent>(levels[Math.Min(index, levels.Count - 1)]).MapId;
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapUid == map)
                MoveTo(uid, refuge);
        }

        _zLevels.WfRestack(levels, ground);
        QueueDel(map);
        return true;
    }

    private void OnSave(MappingZLevelsSaveEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!CanUse(session) || !TryGetPlayerMap(session, out _, out var map))
            return;

        if (!SaveName.IsMatch(ev.Name))
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-maps-bad-name"), session);
            return;
        }

        var levels = GetLevels(map, out var ground);
        var saved = TrySaveStack(levels, ground, ev.Name);
        var folder = new ResPath(MappingZLevels.StackPath(ev.Name, 0)).Directory;
        _popup.PopupCursor(Loc.GetString(saved ? "wf-mapping-zlevels-saved" : "wf-mapping-zlevels-save-failed",
            ("count", levels.Count), ("path", folder.ToString())), session);

        if (saved)
        {
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{session:player} saved the {levels.Count}-level z-stack of {ToPrettyString(map)} to {folder}");
        }

        _maps.SendList(session);
    }

    /// <summary>
    /// Saves each level to <see cref="MappingZLevels.StackPath"/>, and deletes levels of an older save under the
    /// same name that the stack no longer has, so opening it doesn't bring them back.
    /// </summary>
    public bool TrySaveStack(List<EntityUid> levels, int ground, string name)
    {
        if (!SaveName.IsMatch(name))
            return false;

        var saved = new HashSet<string>();
        var ok = true;
        for (var i = 0; i < levels.Count; i++)
        {
            var path = MappingZLevels.StackPath(name, i - ground);
            ok &= _loader.TrySaveMap(levels[i], new ResPath(path));
            saved.Add(path);
        }

        var folder = new ResPath(MappingZLevels.StackPath(name, 0)).Directory;
        if (!_res.UserData.IsDir(folder))
            return false;

        foreach (var entry in _res.UserData.DirectoryEntries(folder).ToList())
        {
            var path = folder / entry;
            if (MappingZLevels.TryParseLevelFile(entry, out var prefix, out _) && prefix == name &&
                !saved.Contains(path.ToString()))
            {
                _res.UserData.Delete(path);
            }
        }

        return ok;
    }

    private void OnOpen(MappingZLevelsOpenEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!CanUse(session) || !_maps.IsListed(ev.Path))
            return;

        var files = FindStackFiles(new ResPath(ev.Path));
        if (!files.TryGetValue(0, out var groundFile))
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-zlevels-no-ground"), session);
            return;
        }

        // The mapping command opens the ground level and sets the mapper up on it, as the Maps window's Open does.
        var id = 1;
        while (_map.MapExists(new MapId(id)))
        {
            id++;
        }

        var groundId = new MapId(id);
        _console.ExecuteCommand(session, $"mapping {id} \"{groundFile}\"");
        if (!_map.MapExists(groundId))
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-zlevels-open-failed", ("path", groundFile.ToString())), session);
            return;
        }

        var levels = new List<EntityUid>();
        var ground = 0;
        var opts = new DeserializationOptions { StoreYamlUids = true };
        foreach (var (depth, file) in files)
        {
            if (depth == 0)
            {
                ground = levels.Count;
                levels.Add(_map.GetMapOrInvalid(groundId));
                continue;
            }

            if (!_loader.TryLoadMap(file, out var loaded, out _, opts))
            {
                _popup.PopupCursor(Loc.GetString("wf-mapping-zlevels-open-failed", ("path", file.ToString())), session);
                continue;
            }

            levels.Add(loaded.Value.Owner);
        }

        _zLevels.WfRestack(levels, ground);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} opened the {levels.Count}-level z-stack {groundFile} from the Maps window");
    }

    /// <summary>
    /// The level files of the stack <paramref name="path"/> belongs to, by depth. A save in the data folder shadows
    /// the game's file at the same path, as it does when loading.
    /// </summary>
    public SortedDictionary<int, ResPath> FindStackFiles(ResPath path)
    {
        var found = new SortedDictionary<int, ResPath>();
        if (!MappingZLevels.TryParseLevelFile(path.Filename, out var prefix, out _))
            return found;

        var folder = path.Directory;
        var candidates = _res.ContentFindFiles(folder)
            .Where(p => p.Directory == folder)
            .Concat(_res.UserData.Exists(folder) && _res.UserData.IsDir(folder)
                ? _res.UserData.DirectoryEntries(folder).Select(entry => folder / entry)
                : Enumerable.Empty<ResPath>());

        foreach (var candidate in candidates)
        {
            if (MappingZLevels.TryParseLevelFile(candidate.Filename, out var candidatePrefix, out var depth) &&
                candidatePrefix == prefix)
            {
                found[depth] = candidate;
            }
        }

        return found;
    }
}
