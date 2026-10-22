using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Popups;
using Content.Shared._WF.MappingTools;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingTools;

/// <summary>
/// The Maps window's server side: lists map files, sends one's text for the preview, and saves and opens them like
/// the savegrid, savemap, mapping and loadgrid commands.
/// </summary>
public sealed class MappingMapsSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private SharedMapSystem _mapManager = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private TransformSystem _transform = default!;

    public static readonly ResPath SaveFolder = new(MappingMaps.SaveFolder);

    /// <summary>
    /// Files bigger than this aren't sent for preview; whole stations run to several MB.
    /// </summary>
    private const long MaxPreviewBytes = 2 * 1024 * 1024;

    /// <summary>
    /// How deep into the data folder saves are looked for.
    /// </summary>
    private const int MaxSaveDepth = 6;

    private static readonly ResPath ShipsFolder = new("/SharedMaps");
    private static readonly ResPath MapsFolder = new("/Maps");

    /// <summary>Mapping mode's per-player libraries, which only their owners reach.</summary>
    private static readonly ResPath LibraryFolder = new("/Mapping/Library");
    private static readonly Regex SaveName = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MappingMapsListRequestEvent>(OnListRequest);
        SubscribeNetworkEvent<MappingMapsPreviewRequestEvent>(OnPreviewRequest);
        SubscribeNetworkEvent<MappingMapsSaveEvent>(OnSave);
        SubscribeNetworkEvent<MappingMapsLoadEvent>(OnLoad);
    }

    private bool CanUse(ICommonSession session)
    {
        return _admin.HasAdminFlag(session, AdminFlags.Mapping);
    }

    /// <summary>
    /// Every map file a mapper can open: their saves first, then the game's ship grids and maps.
    /// </summary>
    public List<MappingMapFile> ListFiles()
    {
        var saved = new List<ResPath>();
        FindSaved(ResPath.Root, saved, 0);

        var files = saved.Select(p => p.ToString()).Order()
            .Select(p => new MappingMapFile(p, MappingMapSource.Saved))
            .ToList();

        // A save at the same path shadows the game's file when loading, so it's listed once.
        var shadowed = files.Select(f => f.Path).ToHashSet();
        foreach (var (folder, source) in new[] { (ShipsFolder, MappingMapSource.Ships), (MapsFolder, MappingMapSource.Maps) })
        {
            files.AddRange(_res.ContentFindFiles(folder)
                .Where(p => p.Extension == "yml")
                .Select(p => p.ToString())
                .Where(p => !shadowed.Contains(p))
                .Order()
                .Select(p => new MappingMapFile(p, source)));
        }

        return files;
    }

    /// <summary>
    /// Collects the .yml files in the server's data folder.
    /// </summary>
    private void FindSaved(ResPath dir, List<ResPath> found, int depth)
    {
        foreach (var entry in _res.UserData.DirectoryEntries(dir))
        {
            var path = dir / entry;
            if (_res.UserData.IsDir(path))
            {
                if (depth < MaxSaveDepth && path != LibraryFolder)
                    FindSaved(path, found, depth + 1);
            }
            else if (path.Extension == "yml")
            {
                found.Add(path);
            }
        }
    }

    /// <summary>
    /// Whether <see cref="ListFiles"/> offers a path, so requests can't reach anything else on disk.
    /// </summary>
    public bool IsListed(string path)
    {
        var res = new ResPath(path);
        if (!res.IsRooted || res.Extension != "yml" || path.Contains(".."))
            return false;

        return _res.UserData.Exists(res) ||
               (res.TryRelativeTo(ShipsFolder, out _) || res.TryRelativeTo(MapsFolder, out _)) && _res.ContentFileExists(res);
    }

    private void OnListRequest(MappingMapsListRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CanUse(args.SenderSession))
            return;

        SendList(args.SenderSession);
    }

    public void SendList(ICommonSession session)
    {
        var folder = _res.UserData.RootDir is { } root
            ? Path.Combine(root, SaveFolder.ToRelativePath().ToString())
            : SaveFolder.ToString();

        RaiseNetworkEvent(new MappingMapsListEvent(ListFiles(), folder), session);
    }

    private void OnPreviewRequest(MappingMapsPreviewRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!CanUse(args.SenderSession))
            return;

        RaiseNetworkEvent(ReadForPreview(ev.Path), args.SenderSession);
    }

    /// <summary>
    /// A listed file's text for the preview, read like the map loader does: saves first, then game files.
    /// </summary>
    public MappingMapsPreviewEvent ReadForPreview(string path)
    {
        if (!IsListed(path))
            return new MappingMapsPreviewEvent(path, null, "wf-mapping-maps-preview-missing");

        var res = new ResPath(path);
        using var stream = _res.UserData.Exists(res) ? _res.UserData.OpenRead(res) : _res.ContentFileRead(res);
        if (stream.CanSeek && stream.Length > MaxPreviewBytes)
            return new MappingMapsPreviewEvent(path, null, "wf-mapping-maps-preview-too-large");

        using var reader = new StreamReader(stream);
        return new MappingMapsPreviewEvent(path, reader.ReadToEnd(), null);
    }

    private void OnSave(MappingMapsSaveEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!CanUse(session))
            return;

        if (!SaveName.IsMatch(ev.Name))
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-maps-bad-name"), session);
            return;
        }

        if (session.AttachedEntity is not { } player)
            return;

        var xform = Transform(player);
        EntityUid? target = ev.WholeMap ? xform.MapUid : xform.GridUid;
        if (target == null)
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-maps-no-grid"), session);
            return;
        }

        var saved = TrySave(target.Value, ev.Name, ev.WholeMap, out var path);
        _popup.PopupCursor(Loc.GetString(saved ? "wf-mapping-maps-saved" : "wf-mapping-maps-save-failed",
            ("path", path.ToString())), session);

        if (saved)
        {
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{session:player} saved {ToPrettyString(target.Value)} to {path} from the Maps window");
        }

        SendList(session);
    }

    /// <summary>
    /// Saves a grid, or with <paramref name="wholeMap"/> a map, into the save folder.
    /// </summary>
    public bool TrySave(EntityUid target, string name, bool wholeMap, out ResPath path)
    {
        path = SaveFolder / $"{name}.yml";
        if (!SaveName.IsMatch(name))
            return false;

        return wholeMap ? _loader.TrySaveMap(target, path) : _loader.TrySaveGrid(target, path);
    }

    private void OnLoad(MappingMapsLoadEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (!CanUse(session) || !IsListed(ev.Path))
            return;

        if (!ev.Here)
        {
            // The mapping command sets up the fresh map, the aghost and the tools.
            var id = 1;
            while (_mapManager.MapExists(new MapId(id)))
            {
                id++;
            }

            _console.ExecuteCommand(session, $"mapping {id} \"{ev.Path}\"");
            return;
        }

        if (session.AttachedEntity is not { } player)
            return;

        var coords = _transform.GetMapCoordinates(player);
        if (!_loader.TryLoadGrid(coords.MapId, new ResPath(ev.Path), out var loaded, offset: coords.Position))
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-maps-load-here-failed"), session);
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} loaded {ev.Path} as {ToPrettyString(loaded.Value.Owner):grid} from the Maps window");
    }
}
