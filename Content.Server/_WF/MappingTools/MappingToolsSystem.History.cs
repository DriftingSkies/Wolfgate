using System.Linq;
using System.Numerics;
using Content.Server.Decals;
using Content.Shared.Administration;
using Content.Shared.Decals;
using Content.Shared.GameTicking;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Placement;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _players = default!;

    /// <summary>
    /// Edits kept per mapper when nothing sets a lower limit; the oldest are dropped past this.
    /// </summary>
    private const int DefaultMaxHistory = 100;

    private sealed class History
    {
        public readonly List<MappingEdit> Undo = new();
        public readonly List<MappingEdit> Redo = new();
    }

    private readonly Dictionary<ICommonSession, History> _histories = new();

    /// <summary>
    /// The last tile change seen, claimed by the <see cref="PlacementTileEvent"/> the engine raises right after it.
    /// </summary>
    private MappingTileChange? _lastTileChange;

    private readonly List<PendingDecal> _pendingDecals = new();

    /// <summary>
    /// A decal placement the decal system hasn't handled yet, with the decals already there.
    /// </summary>
    private sealed record PendingDecal(ICommonSession Session, EntityUid Grid, Vector2 Position, string Id, HashSet<uint> Before);

    private void InitializeHistory()
    {
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<PlacementTileEvent>(OnPlacementTile);
        SubscribeLocalEvent<PlacementEntityEvent>(OnPlacementEntity);
        SubscribeNetworkEvent<RequestDecalPlacementEvent>(OnDecalPlacement, before: [typeof(DecalSystem)]);
        SubscribeNetworkEvent<RequestDecalRemovalEvent>(OnDecalRemoval, before: [typeof(DecalSystem)]);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

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

        _lastTileChange = null;

        foreach (var pending in _pendingDecals)
        {
            if (TerminatingOrDeleted(pending.Grid))
                continue;

            var edit = new MappingEdit("wf-mapping-tools-edit-decal");
            foreach (var (id, decal) in _decals.GetDecalsInRange(pending.Grid, pending.Position, 1.5f))
            {
                if (!pending.Before.Contains(id) && decal.Id == pending.Id)
                    edit.DecalsAdded.Add(new MappingDecal(pending.Grid, decal));
            }

            RecordPlacement(pending.Session, edit);
        }

        _pendingDecals.Clear();
    }

    /// <summary>
    /// Applies a new edit and pushes it onto the mapper's history. Refuses one that would lay tiles over another grid.
    /// </summary>
    private bool Commit(ICommonSession session, MappingEdit edit)
    {
        if (edit.IsEmpty)
            return false;

        if (FillsOverOtherGrid(edit, forward: true))
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-tools-overlaps-grid"), session);
            return false;
        }

        Apply(edit, forward: true);
        Push(session, edit);
        RaiseEdited(edit);
        SendLinkSnapshots(session, edit);
        return true;
    }

    /// <summary>
    /// The maps an edit touches.
    /// </summary>
    private HashSet<EntityUid> MapsOf(MappingEdit edit)
    {
        var maps = new HashSet<EntityUid>();

        void Add(EntityUid uid)
        {
            if (Exists(uid) && Transform(uid).MapUid is { } map)
                maps.Add(map);
        }

        foreach (var tile in edit.Tiles)
        {
            Add(tile.Grid);
        }

        foreach (var move in edit.Moves)
        {
            Add(move.Entity.Uid);
            Add(move.Old.Parent);
            Add(move.New.Parent);
        }

        foreach (var group in edit.Created.Concat(edit.Deleted))
        {
            foreach (var root in group.Roots)
            {
                Add(root.Entity.Uid);
                Add(root.Pose.Parent);
            }
        }

        foreach (var decal in edit.DecalsRemoved.Concat(edit.DecalsAdded))
        {
            Add(decal.Grid);
        }

        foreach (var change in edit.PipeColors)
        {
            Add(change.Entity.Uid);
        }

        foreach (var change in edit.Links)
        {
            Add(change.Source.Uid);
            Add(change.Sink.Uid);
        }

        foreach (var change in edit.DeviceLists)
        {
            Add(change.Holder.Uid);
        }

        foreach (var change in edit.Access)
        {
            Add(change.Reader.Uid);
        }

        return maps;
    }

    private void RaiseEdited(MappingEdit edit)
    {
        foreach (var map in MapsOf(edit))
        {
            RaiseLocalEvent(new WFMappingEditedEvent(map));
        }
    }

    /// <summary>
    /// Drops a mapper's undo history, so it can't reach maps they no longer work on. The clipboard stays.
    /// </summary>
    public void ForgetHistory(ICommonSession session)
    {
        _histories.Remove(session);
    }

    private void Push(ICommonSession session, MappingEdit edit)
    {
        var history = _histories.GetOrNew(session);
        history.Redo.Clear();
        history.Undo.Add(edit);
        TrimHistory(session, history);
    }

    /// <summary>
    /// Drops a mapper's oldest edits past their step and byte limits, always keeping the newest.
    /// </summary>
    private void TrimHistory(ICommonSession session, History history)
    {
        var limits = HistoryLimits(session);
        while (history.Undo.Count > Math.Max(1, limits.MaxSteps))
        {
            history.Undo.RemoveAt(0);
        }

        if (limits.MaxBytes == long.MaxValue)
            return;

        var bytes = history.Undo.Sum(e => e.Bytes()) + history.Redo.Sum(e => e.Bytes());
        while (bytes > limits.MaxBytes && history.Undo.Count > 1)
        {
            bytes -= history.Undo[0].Bytes();
            history.Undo.RemoveAt(0);
        }
    }

    /// <summary>
    /// A mapper's history limits: what a <see cref="WFMappingCanEditEvent"/> handler sets, or the default for Mapping
    /// admins and when nothing does.
    /// </summary>
    private WFMappingHistoryLimits HistoryLimits(ICommonSession session)
    {
        var unlimited = new WFMappingHistoryLimits(DefaultMaxHistory, long.MaxValue);
        if (_admin.HasAdminFlag(session, AdminFlags.Mapping))
            return unlimited;

        var ev = new WFMappingCanEditEvent(session, EntityUid.Invalid, WFMappingEditKind.Limits) { Quiet = true };
        RaiseLocalEvent(ev);
        return ev.HistoryLimits ?? unlimited;
    }

    /// <summary>
    /// Records an edit the placement menu already made, merged with others from the same tick.
    /// </summary>
    private void RecordPlacement(ICommonSession session, MappingEdit edit)
    {
        if (edit.IsEmpty || !CanUse(session))
            return;

        var tick = _timing.CurTick;
        if (_histories.TryGetValue(session, out var history) &&
            history.Redo.Count == 0 &&
            history.Undo.Count > 0 &&
            history.Undo[^1] is { PlacementTick: { } last } previous &&
            last == tick)
        {
            previous.Merge(edit);
            TrimHistory(session, history);
            return;
        }

        edit.PlacementTick = tick;
        Push(session, edit);
    }

    /// <summary>
    /// Undoes the mapper's last edit, or redoes their last undone one.
    /// </summary>
    public void StepHistory(ICommonSession session, bool redo)
    {
        if (!_histories.TryGetValue(session, out var history) || (redo ? history.Redo : history.Undo).Count == 0)
        {
            _popup.PopupCursor(Loc.GetString(redo ? "wf-mapping-tools-nothing-to-redo" : "wf-mapping-tools-nothing-to-undo"), session);
            return;
        }

        var (from, to) = redo ? (history.Redo, history.Undo) : (history.Undo, history.Redo);
        var edit = from[^1];
        var (added, removed, newGrid) = StepCounts(edit, redo);
        foreach (var map in MapsOf(edit))
        {
            var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Edit)
            {
                Entities = Math.Max(0, added - removed),
                TouchedEntities = added + removed + edit.Moves.Count + edit.SettingChanges,
                NewGrid = newGrid,
            };

            if (!CanEdit(check))
                return;
        }

        if (FillsOverOtherGrid(edit, forward: redo))
        {
            _popup.PopupCursor(Loc.GetString("wf-mapping-tools-overlaps-grid"), session);
            return;
        }

        from.RemoveAt(from.Count - 1);
        Apply(edit, forward: redo);
        to.Add(edit);
        RaiseEdited(edit);
        SendLinkSnapshots(session, edit);

        _popup.PopupCursor(Loc.GetString(redo ? "wf-mapping-tools-redid" : "wf-mapping-tools-undid",
            ("edit", Loc.GetString(edit.Name))), session);
    }

    /// <summary>
    /// What a history step would do: entities it recreates and deletes, contents included, and whether it brings a
    /// grid back.
    /// </summary>
    private (int Added, int Removed, bool NewGrid) StepCounts(MappingEdit edit, bool redo)
    {
        var (restoring, removing) = redo ? (edit.Created, edit.Deleted) : (edit.Deleted, edit.Created);
        var added = 0;
        var removed = 0;
        var newGrid = false;

        foreach (var group in restoring)
        {
            if (group.Data == null || group.Roots.All(r => !TerminatingOrDeleted(r.Entity.Uid)))
                continue;

            added += group.EntityCount;
            newGrid |= group.Roots.Any(r => r.IsGrid && TerminatingOrDeleted(r.Entity.Uid));
        }

        foreach (var group in removing)
        {
            foreach (var root in group.Roots)
            {
                if (!TerminatingOrDeleted(root.Entity.Uid))
                    removed += Tree(root.Entity.Uid).Count;
            }
        }

        return (added, removed, newGrid);
    }

    private void OnTileChanged(ref TileChangedEvent ev)
    {
        if (_applying || ev.Changes.Length == 0)
            return;

        var change = ev.Changes[^1];
        _lastTileChange = new MappingTileChange(ev.Entity, change.GridIndices, change.OldTile, change.NewTile);
    }

    private void OnPlacementTile(PlacementTileEvent ev)
    {
        if (_lastTileChange is not { } change || change.New.TypeId != ev.TileType)
            return;

        _lastTileChange = null;

        // The engine raises the event for a tile that didn't change too, so the last change may be someone else's.
        var coords = ev.Coordinates;
        if (!coords.IsValid(EntityManager) || !TryComp(change.Grid, out MapGridComponent? changed))
            return;

        var placedOn = HasComp<MapGridComponent>(coords.EntityId)
            ? coords.EntityId
            : _map.TryFindGridAt(_transform.ToMapCoordinates(coords), out var found, out _) ? found : EntityUid.Invalid;
        if (placedOn != change.Grid || _map.TileIndicesFor(change.Grid, changed, coords) != change.Indices)
            return;

        if (!_players.TryGetSessionById(ev.PlacerNetUserId, out var session))
            return;

        var edit = new MappingEdit("wf-mapping-tools-edit-tile");
        edit.Tiles.Add(change);
        RecordPlacement(session, edit);
    }

    private void OnPlacementEntity(PlacementEntityEvent ev)
    {
        if (!_players.TryGetSessionById(ev.PlacerNetUserId, out var session) || !CanUse(session))
            return;

        var uid = ev.EditedEntity;
        var parent = Transform(uid).ParentUid;
        if (!IsSelectable(uid) || !HasComp<MapGridComponent>(parent) && !HasComp<MapComponent>(parent))
            return;

        var group = new MappingEntityGroup();
        group.Roots.Add(new MappingGroupRoot(GetRef(uid), -1, PoseOf(uid)));

        MappingEdit edit;
        if (ev.PlacementEventAction == PlacementEventAction.Create)
        {
            edit = new MappingEdit("wf-mapping-tools-edit-place");
            edit.Created.Add(group);
        }
        else
        {
            // Raised just before the engine deletes it.
            edit = new MappingEdit("wf-mapping-tools-edit-erase");
            SaveGroup(group, [uid]);
            edit.Deleted.Add(group);
        }

        RecordPlacement(session, edit);
    }

    private void OnDecalPlacement(RequestDecalPlacementEvent ev, EntitySessionEventArgs args)
    {
        if (!CanUse(args.SenderSession))
            return;

        var coords = GetCoordinates(ev.Coordinates);
        if (!coords.IsValid(EntityManager) || _transform.GetGrid(coords) is not { } grid || !HasComp<MapGridComponent>(grid))
            return;

        var position = _transform.WithEntityId(coords, grid).Position;
        var before = new HashSet<uint>();
        foreach (var (id, _) in _decals.GetDecalsInRange(grid, position, 1.5f))
        {
            before.Add(id);
        }

        _pendingDecals.Add(new PendingDecal(args.SenderSession, grid, position, ev.Decal.Id, before));
    }

    private void OnDecalRemoval(RequestDecalRemovalEvent ev, EntitySessionEventArgs args)
    {
        if (!CanUse(args.SenderSession))
            return;

        var coords = GetCoordinates(ev.Coordinates);
        if (!coords.IsValid(EntityManager) || _transform.GetGrid(coords) is not { } grid)
            return;

        // The decal system only removes for Spawn admins, and mapping mode for mappers on their own maps.
        if (!_admin.HasAdminFlag(args.SenderSession, AdminFlags.Spawn) &&
            (_transform.GetMap(coords) is not { } map ||
             !CanEdit(new WFMappingCanEditEvent(args.SenderSession, map, WFMappingEditKind.Edit) { Quiet = true })))
        {
            return;
        }

        // Mirrors the decal system, which removes every decal in range of the raw position.
        var edit = new MappingEdit("wf-mapping-tools-edit-decal");
        foreach (var (_, decal) in _decals.GetDecalsInRange(grid, ev.Coordinates.Position))
        {
            edit.DecalsRemoved.Add(new MappingDecal(grid, decal));
        }

        RecordPlacement(args.SenderSession, edit);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        _histories.Remove(args.Session);
        _clipboards.Remove(args.Session);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _histories.Clear();
        _clipboards.Clear();
        _refs.Clear();
        _lastTileChange = null;
        _pendingDecals.Clear();
    }
}
