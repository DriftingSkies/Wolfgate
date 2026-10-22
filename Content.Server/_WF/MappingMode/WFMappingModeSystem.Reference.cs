using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Database;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    /// <summary>A reference loaded aside on a scratch map, waiting for its mapper to place it.</summary>
    private sealed class PendingReference(int token, int sessionId, EntityUid scratch, string path)
    {
        public readonly int Token = token;
        public readonly int SessionId = sessionId;
        public readonly EntityUid Scratch = scratch;
        public readonly string Path = path;
        public readonly List<(EntityUid Grid, WFMappingFootprintGrid Footprint)> Grids = new();
        public int Entities;
        public TimeSpan Expires;
    }

    /// <summary>A file loaded aside onto a scratch map: the map, its grids and the entities on them.</summary>
    public sealed class ScratchLoad(EntityUid map)
    {
        public readonly EntityUid Map = map;
        public readonly List<EntityUid> Grids = new();
        public int Entities;

        /// <summary>Entities deleted for carrying a denied component, contents included.</summary>
        public int Stripped;
    }

    /// <summary>Why a load was refused: a Fluent id and its arguments.</summary>
    public readonly record struct LoadRefusal(string Key, (string, object)[] Args)
    {
        public static LoadRefusal Of(string key, params (string, object)[] args)
        {
            return new LoadRefusal(key, args);
        }
    }

    private readonly Dictionary<NetUserId, PendingReference> _references = new();
    private int _nextReferenceToken = 1;

    /// <summary>Game time before which no reference may load, whoever asks; loads block the tick.</summary>
    private TimeSpan _nextReferenceLoad;

    private static readonly TimeSpan ReferenceLifetime = TimeSpan.FromSeconds(60);

    /// <summary>Folder in the server's data directory holding every player's library.</summary>
    public static readonly ResPath LibraryFolder = WFMappingLibraryNames.Root;

    private const int MaxLibraryDepth = 4;

    private void InitializeReference()
    {
        SubscribeNetworkEvent<WFMappingReferenceListRequest>(OnReferenceList);
        SubscribeNetworkEvent<WFMappingReferencePickRequest>(OnReferencePick);
        SubscribeNetworkEvent<WFMappingReferenceConfirmRequest>(OnReferenceConfirm);
        SubscribeNetworkEvent<WFMappingReferenceCancelRequest>(OnReferenceCancel);
    }

    /// <summary>
    /// The files a player may load as a reference: the game's ships and maps and their own library levels, or every
    /// listed file for a Mapping admin.
    /// </summary>
    public List<MappingMapFile> ReferenceFiles(ICommonSession player)
    {
        var files = _mapFiles.ListFiles();
        if (IsAdminMapper(player))
            return files;

        var result = files.Where(f => f.Source is MappingMapSource.Ships or MappingMapSource.Maps).ToList();
        var own = new List<ResPath>();
        FindLibraryFiles(LibraryFolder / player.UserId.ToString(), own, 0);
        result.InsertRange(0, own.Select(p => p.ToString()).Order().Select(p => new MappingMapFile(p, MappingMapSource.Saved)));
        return result;
    }

    /// <summary>Level files under a library folder; the grid and vessel files beside them are left out.</summary>
    private void FindLibraryFiles(ResPath dir, List<ResPath> found, int depth)
    {
        if (!_res.UserData.IsDir(dir))
            return;

        foreach (var entry in _res.UserData.DirectoryEntries(dir))
        {
            var path = dir / entry;
            if (_res.UserData.IsDir(path))
            {
                if (depth < MaxLibraryDepth)
                    FindLibraryFiles(path, found, depth + 1);
            }
            else if (WFMappingLibraryNames.TryParseLevelFile(entry, out _))
            {
                found.Add(path);
            }
        }
    }

    private void OnReferenceList(WFMappingReferenceListRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out _) || !TryThrottle(_listThrottle, player, "reference list"))
            return;

        RaiseNetworkEvent(new WFMappingReferenceListEvent(ReferenceFiles(player)), player);
    }

    private void OnReferencePick(WFMappingReferencePickRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out var session) || !TryThrottle(_pickThrottle, player, "reference pick"))
            return;

        if (ev.Path.Length > WFMappingModeLimits.MaxPathLength || ReferenceFiles(player).All(f => f.Path != ev.Path))
        {
            Refuse(player, "wf-mapping-mode-reference-not-listed");
            return;
        }

        var path = new ResPath(ev.Path);
        var maxBytes = _cfg.GetCVar(WFMappingModeCVars.MaxReferenceBytes);
        if (FileSize(path) is not { } size || size > maxBytes)
        {
            Refuse(player, "wf-mapping-mode-reference-too-large",
                ("max", (maxBytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture)));
            return;
        }

        if (!TryReserveLoad())
        {
            Refuse(player, "wf-mapping-mode-reference-busy");
            return;
        }

        CancelReference(player.UserId);
        if (!TryLoadOntoScratch(path, player, session, false, "wf-mapping-mode-reference-failed", out var load,
                out var refusal))
        {
            Refuse(player, refusal.Key, refusal.Args);
            return;
        }

        var pending = new PendingReference(_nextReferenceToken++, session.Id, load.Map, ev.Path)
        {
            Expires = _timing.CurTime + ReferenceLifetime,
            Entities = load.Entities,
        };

        AddFootprints(pending, load.Grids);
        _references[player.UserId] = pending;
        RaiseNetworkEvent(new WFMappingReferenceFootprintEvent(pending.Token,
            pending.Grids.Select(g => g.Footprint).ToList()), player);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} loaded {ev.Path} as a reference for mapping session {session.Code}");
        if (load.Stripped > 0)
            Notify(player, true, "wf-mapping-mode-denied-stripped", ("count", load.Stripped));
    }

    private long? FileSize(ResPath path)
    {
        if (_res.UserData.Exists(path))
        {
            using var stream = _res.UserData.OpenRead(path);
            return stream.Length;
        }

        if (!_res.TryContentFileRead(path, out var content))
            return null;

        using (content)
        {
            return content.CanSeek ? content.Length : null;
        }
    }

    /// <summary>
    /// Takes the server-wide load slot, shared by reference loads and uploads since a load blocks the tick; false
    /// while the last load's <c>reference_cooldown</c> runs.
    /// </summary>
    public bool TryReserveLoad()
    {
        var now = _timing.CurTime;
        if (now < _nextReferenceLoad)
            return false;

        _nextReferenceLoad = now + TimeSpan.FromSeconds(Math.Max(0f, _cfg.GetCVar(WFMappingModeCVars.ReferenceCooldown)));
        return true;
    }

    /// <summary>Loads a file onto a new scratch map; see the <see cref="MappingDataNode"/> overload.</summary>
    public bool TryLoadOntoScratch(ResPath path, ICommonSession player, WFMappingSession? session, bool checkArea,
        string failKey, [NotNullWhen(true)] out ScratchLoad? load, out LoadRefusal refusal)
    {
        return TryLoadOntoScratch(options => _loader.TryLoadGeneric(path, out _, options), path.ToString(), player,
            session, checkArea, failKey, out load, out refusal);
    }

    /// <summary>
    /// Loads map data onto a new paused, uninitialised scratch map, deletes loose and denied entities, and checks the
    /// grid, entity and per-tile caps against <paramref name="session"/>, or those of a new session when null; with
    /// <paramref name="checkArea"/> every grid must also lie inside <c>map_size</c> where it is. The caller owns the
    /// scratch map on success; on failure it is gone and <paramref name="refusal"/> says why.
    /// </summary>
    public bool TryLoadOntoScratch(MappingDataNode data, string source, ICommonSession player,
        WFMappingSession? session, bool checkArea, string failKey, [NotNullWhen(true)] out ScratchLoad? load,
        out LoadRefusal refusal)
    {
        return TryLoadOntoScratch(options => _loader.TryLoadGeneric(data, source, out _, options), source, player,
            session, checkArea, failKey, out load, out refusal);
    }

    private bool TryLoadOntoScratch(Func<MapLoadOptions, bool> loadWith, string source, ICommonSession player,
        WFMappingSession? session, bool checkArea, string failKey, [NotNullWhen(true)] out ScratchLoad? load,
        out LoadRefusal refusal)
    {
        load = null;
        var scratch = _map.CreateMap(out var scratchId, runMapInit: false);
        RegisterMap(scratch, WFMappingMapKind.Scratch, session?.Id ?? 0);
        var loaded = new ScratchLoad(scratch);
        try
        {
            if (FillScratch(loaded, loadWith, scratchId, player.UserId, session, checkArea, failKey, out refusal))
            {
                load = loaded;
                return true;
            }

            Log.Info($"Refused {source} for {player} after loading it aside: {refusal.Key}");
        }
        catch (Exception e)
        {
            Log.Error($"Loading {source} onto a scratch map for {player} failed: {e}");
            refusal = LoadRefusal.Of(failKey);
        }

        if (!TerminatingOrDeleted(scratch))
            Del(scratch);

        return false;
    }

    private bool FillScratch(ScratchLoad load, Func<MapLoadOptions, bool> loadWith, MapId scratchId, NetUserId user,
        WFMappingSession? session, bool checkArea, string failKey, out LoadRefusal refusal)
    {
        var options = new MapLoadOptions
        {
            MergeMap = scratchId,
            DeserializationOptions = new DeserializationOptions
            {
                StoreYamlUids = true,
                InitializeMaps = false,
                PauseMaps = true,
            },
        };

        if (!loadWith(options))
        {
            refusal = LoadRefusal.Of(failKey);
            return false;
        }

        var grids = load.Grids;
        var loose = new List<EntityUid>();
        var children = Transform(load.Map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            (HasComp<MapGridComponent>(child) ? grids : loose).Add(child);
        }

        // The caps come first, on everything loaded, so an oversized file costs nothing more than its load.
        if (!CheckLoadCaps(user, session, grids.Count, CountTree(load.Map) - 1, out refusal))
            return false;

        foreach (var uid in loose)
        {
            Del(uid);
        }

        if (grids.Count == 0)
        {
            refusal = LoadRefusal.Of("wf-mapping-mode-reference-empty");
            return false;
        }

        var denied = new List<EntityUid>();
        foreach (var grid in grids)
        {
            CollectDenied(grid, denied);
        }

        foreach (var uid in denied)
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }

        load.Stripped = denied.Count;
        load.Entities = grids.Sum(CountTree);
        if (!CheckLoadCaps(user, session, grids.Count, load.Entities, out refusal))
            return false;

        var maxPerTile = _cfg.GetCVar(WFMappingModeCVars.MaxEntitiesPerTile);
        var maxDecals = _cfg.GetCVar(WFMappingModeCVars.MaxDecalsPerTile);
        foreach (var grid in grids)
        {
            var comp = Comp<MapGridComponent>(grid);
            if (MaxPerCell(grid, comp) > maxPerTile)
            {
                refusal = LoadRefusal.Of("wf-mapping-mode-limit-entities-per-tile", ("max", maxPerTile));
                return false;
            }

            if (MaxDecalsPerCell(grid, comp) > maxDecals)
            {
                refusal = LoadRefusal.Of("wf-mapping-mode-limit-decals-per-tile", ("max", maxDecals));
                return false;
            }

            if (checkArea && !InBounds(_transform.GetWorldMatrix(grid).TransformBox(comp.LocalAABB)))
            {
                refusal = LoadRefusal.Of("wf-mapping-mode-limit-area", ("size", MapSize));
                return false;
            }
        }

        refusal = default;
        return true;
    }

    /// <summary>Works out a loaded reference's footprint, relative to the floored centre of its grids' bounds.</summary>
    private void AddFootprints(PendingReference pending, List<EntityUid> grids)
    {
        Box2? bounds = null;
        foreach (var grid in grids)
        {
            var box = _transform.GetWorldMatrix(grid).TransformBox(Comp<MapGridComponent>(grid).LocalAABB);
            bounds = bounds is { } b ? b.Union(box) : box;
        }

        var centre = bounds?.Center ?? Vector2.Zero;
        var anchor = new Vector2(MathF.Floor(centre.X), MathF.Floor(centre.Y));
        foreach (var grid in grids)
        {
            var comp = Comp<MapGridComponent>(grid);
            var cells = new List<Vector2i>();
            var tiles = _map.GetAllTiles(grid, comp);
            while (tiles.MoveNext(out var tile))
            {
                cells.Add(tile.Value.GridIndices);
            }

            var (position, rotation) = _transform.GetWorldPositionRotation(grid);
            pending.Grids.Add((grid, new WFMappingFootprintGrid(position - anchor, rotation, WFMappingFootprint.Merge(cells))));
        }
    }

    /// <summary>Deletes everything on a map that carries a denied component; the count deleted.</summary>
    public int StripDenied(EntityUid map)
    {
        var denied = new List<EntityUid>();
        var children = Transform(map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            CollectDenied(child, denied);
        }

        foreach (var uid in denied)
        {
            if (!TerminatingOrDeleted(uid))
                Del(uid);
        }

        return denied.Count;
    }

    private void CollectDenied(EntityUid root, List<EntityUid> denied)
    {
        var stack = new Stack<EntityUid>();
        stack.Push(root);
        while (stack.TryPop(out var uid))
        {
            if (HasDeniedComponent(uid))
            {
                denied.Add(uid);
                continue;
            }

            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                stack.Push(child);
            }
        }
    }

    /// <summary>The most entities parented to the grid in any one cell.</summary>
    private int MaxPerCell(EntityUid grid, MapGridComponent comp)
    {
        var counts = new Dictionary<Vector2i, int>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            var cell = MappingToolsMath.CellOf(Transform(child).LocalPosition);
            counts[cell] = counts.GetValueOrDefault(cell) + 1;
        }

        return counts.Count == 0 ? 0 : counts.Values.Max();
    }

    /// <summary>The most decals in any one cell of the grid.</summary>
    private int MaxDecalsPerCell(EntityUid grid, MapGridComponent comp)
    {
        var counts = new Dictionary<Vector2i, int>();
        foreach (var (_, decal) in _decals.GetDecalsIntersecting(grid, comp.LocalAABB.Enlarged(1f)))
        {
            var cell = MappingToolsMath.CellOf(decal.Coordinates + new Vector2(0.5f));
            counts[cell] = counts.GetValueOrDefault(cell) + 1;
        }

        return counts.Count == 0 ? 0 : counts.Values.Max();
    }

    /// <summary>
    /// Whether a reference fits the session's grid and entity caps, counting the references other members hold
    /// waiting to be placed; tells the player when it doesn't.
    /// </summary>
    private bool CheckReferenceCaps(ICommonSession player, WFMappingSession session, int grids, int entities)
    {
        if (CheckLoadCaps(player.UserId, session, grids, entities, out var refusal))
            return true;

        Refuse(player, refusal.Key, refusal.Args);
        return false;
    }

    /// <summary>
    /// Whether a load fits the session's grid and entity caps, with the references other members hold waiting to be
    /// placed, or the caps a new session starts with when <paramref name="session"/> is null.
    /// </summary>
    private bool CheckLoadCaps(NetUserId user, WFMappingSession? session, int grids, int entities,
        out LoadRefusal refusal)
    {
        var gridCap = _cfg.GetCVar(WFMappingModeCVars.MaxGridsPerSession);
        var existingGrids = 0;
        var existingEntities = 0;
        if (session != null)
        {
            foreach (var (other, pending) in _references)
            {
                if (other == user || pending.SessionId != session.Id)
                    continue;

                existingGrids += pending.Grids.Count;
                existingEntities += pending.Entities;
            }

            gridCap = GridCap(session);
            existingGrids += CountGrids(session);
            existingEntities += EntityCount(session);
        }

        if (existingGrids + grids > gridCap)
        {
            refusal = LoadRefusal.Of("wf-mapping-mode-limit-grids", ("max", gridCap));
            return false;
        }

        var maxEntities = _cfg.GetCVar(WFMappingModeCVars.MaxEntities);
        if (existingEntities + entities > maxEntities)
        {
            refusal = LoadRefusal.Of("wf-mapping-mode-limit-entities", ("max", maxEntities));
            return false;
        }

        refusal = default;
        return true;
    }

    private void OnReferenceConfirm(WFMappingReferenceConfirmRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled ||
            !TryGetPresent(player, out var session) ||
            !_references.TryGetValue(player.UserId, out var pending) ||
            pending.Token != ev.Token ||
            pending.SessionId != session.Id ||
            !TryThrottle(_confirmThrottle, player, "reference confirm"))
        {
            return;
        }

        if (player.AttachedEntity is not { } observer ||
            Transform(observer).MapUid is not { } map ||
            !IsSessionMap(map, session))
        {
            Refuse(player, "wf-mapping-mode-not-your-map");
            return;
        }

        if (!float.IsFinite(ev.Offset.X) || !float.IsFinite(ev.Offset.Y))
            return;

        var turns = MappingToolsMath.Normalize(ev.Turns);
        var bounds = WFMappingFootprint.Bounds(pending.Grids.Select(g => g.Footprint), turns).Translated(ev.Offset);
        if (!InBounds(bounds))
        {
            Refuse(player, "wf-mapping-mode-limit-area", ("size", MapSize));
            return;
        }

        if (!CheckReferenceCaps(player, session, pending.Grids.Count, pending.Entities))
            return;

        foreach (var (grid, footprint) in pending.Grids)
        {
            if (TerminatingOrDeleted(grid))
                continue;

            var (position, rotation) = WFMappingFootprint.Place(footprint, ev.Offset, turns);
            _transform.SetCoordinates((grid, Transform(grid), MetaData(grid)), new EntityCoordinates(map, position),
                rotation: rotation);
        }

        _references.Remove(player.UserId);
        Del(pending.Scratch);
        MoveToLargest(player, map, pending.Grids.Select(g => g.Grid));

        MarkDirty(session);
        session.CountedAt = TimeSpan.Zero;
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{player:player} placed reference {pending.Path} on {ToPrettyString(map)} in mapping session {session.Code}");
        Notify(player, true, "wf-mapping-mode-reference-placed", ("count", pending.Grids.Count));
    }

    private void OnReferenceCancel(WFMappingReferenceCancelRequest ev, EntitySessionEventArgs args)
    {
        if (_references.TryGetValue(args.SenderSession.UserId, out var pending) && pending.Token == ev.Token)
            CancelReference(args.SenderSession.UserId);
    }

    /// <summary>Drops a player's pending reference and its scratch map.</summary>
    private void CancelReference(NetUserId user)
    {
        if (!_references.Remove(user, out var pending))
            return;

        if (!TerminatingOrDeleted(pending.Scratch))
            QueueDel(pending.Scratch);
    }

    private void UpdateReferences(TimeSpan now)
    {
        if (_references.Count == 0)
            return;

        foreach (var (user, pending) in _references.ToList())
        {
            if (now < pending.Expires)
                continue;

            CancelReference(user);
            if (_players.TryGetSessionById(user, out var player))
                Refuse(player, "wf-mapping-mode-reference-expired");
        }
    }
}
