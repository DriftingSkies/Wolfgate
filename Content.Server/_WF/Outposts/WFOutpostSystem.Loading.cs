using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._Mono.Cleanup;
using Content.Server._NF.Bank;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Shipyard;
using Content.Server.Database;
using Content.Shared._WF.Outposts;
using Content.Shared.Database;
using Content.Server.GameTicking;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Loading: the owner of a freshly founded outpost brings a save down in its place. The save is loaded on a paused
/// staging map, stripped of what a save never keeps, priced, and paid for; its footprint is drawn on the ground for
/// <c>wf.outposts.load_delay</c> seconds, then it is checked again and swapped in, its console on the founding console's tile.
/// The founding console packs back into its kit. A load called off or blocked is refunded.
/// </summary>
public sealed partial class WFOutpostSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private UsedShipReinitSystem _reinit = default!;
    [Dependency] private IResourceManager _resources = default!;

    private static readonly EntProtoId KitPrototype = "WFOutpostConsoleKit";

    /// <summary>Space between staged saves on the staging map.</summary>
    private const float StageSpacing = 500f;

    private sealed class PendingLoad
    {
        public NetUserId Owner;

        /// <summary>The save slot it comes from; null for a preset.</summary>
        public int? Slot;
        public string Name = string.Empty;
        public int Price;
        public EntityUid Staged;
        public EntityUid StagedConsole;

        /// <summary>Where the save's tile (0, 0) lands, in the founding outpost's tile coordinates.</summary>
        public Vector2i Shift;

        public TimeSpan Lands;
    }

    /// <summary>Loads on their way down, by the founding outpost they replace.</summary>
    private readonly Dictionary<EntityUid, PendingLoad> _loads = new();

    private EntityUid? _stageMap;
    private float _stageOffset;

    private void InitializeLoading()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _loads.Clear();
        ClearDrops();
        _slots.Clear();
        _stageMap = null;
        _stageOffset = 0;
    }

    /// <summary>
    /// Starts bringing a save down in place of a freshly founded outpost: reads it, then stages, prices and charges it.
    /// <paramref name="reason"/> is a loc id when it can't start; later failures are told to the user as they happen.
    /// </summary>
    public bool TryStartLoad(EntityUid user, Entity<WFOutpostComponent> outpost, int slot, out string reason)
    {
        var owner = outpost.Comp.Owner;
        if (!TryComp<ActorComponent>(user, out var actor) || actor.PlayerSession.UserId != owner)
        {
            reason = "wf-outpost-load-not-owner";
            return false;
        }

        if (slot < 0 || slot >= WFOutpostLimits.SaveSlots || IsBusy(owner) || GetSlots(owner) is not { } slots
            || _loads.ContainsKey(outpost))
        {
            reason = "wf-outpost-save-busy";
            return false;
        }

        if (slots[slot] is not { } save)
        {
            reason = "wf-outpost-load-empty";
            return false;
        }

        if (IsInUse(save))
        {
            reason = "wf-outpost-load-in-use";
            return false;
        }

        if (!IsFresh(outpost, out reason))
            return false;

        _busy.Add(owner);
        _ = Read(user, outpost, slot);
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether an outpost is still as it was founded, so a save can take its place: no plates laid and nothing anchored
    /// on it but its console.
    /// </summary>
    private bool IsFresh(Entity<WFOutpostComponent> outpost, out string reason)
    {
        reason = "wf-outpost-load-not-fresh";
        if (!TryComp<MapGridComponent>(outpost, out var grid) || CountTiles((outpost, grid)) > outpost.Comp.FoundingTiles
            || outpost.Comp.Basement != null)
            return false;

        var children = Transform(outpost).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (child != outpost.Comp.Console && Transform(child).Anchored)
                return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Starts bringing a preset down in place of a freshly founded outpost, for the preset's price.
    /// <paramref name="reason"/> is a loc id when it can't start.
    /// </summary>
    public bool TryStartPresetLoad(EntityUid user, Entity<WFOutpostComponent> outpost, string presetId, out string reason)
    {
        var owner = outpost.Comp.Owner;
        if (!TryComp<ActorComponent>(user, out var actor) || actor.PlayerSession.UserId != owner)
        {
            reason = "wf-outpost-load-not-owner";
            return false;
        }

        if (!_proto.TryIndex<WFOutpostPresetPrototype>(presetId, out var preset))
        {
            reason = "wf-outpost-preset-unknown";
            return false;
        }

        if (IsBusy(owner) || _loads.ContainsKey(outpost))
        {
            reason = "wf-outpost-save-busy";
            return false;
        }

        if (!TryReadPreset(preset, out var yaml))
        {
            reason = "wf-outpost-load-failed";
            return false;
        }

        Stage(user, outpost, null, Loc.GetString(preset.Name), yaml, out reason, preset.Price);
        return reason == "wf-outpost-load-started";
    }

    /// <summary>A preset's grid YAML, read from its file.</summary>
    public bool TryReadPreset(WFOutpostPresetPrototype preset, out string yaml)
    {
        try
        {
            yaml = _resources.ContentFileReadAllText(preset.Grid);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to read outpost preset {preset.ID} from {preset.Grid}: {e}");
            yaml = string.Empty;
            return false;
        }
    }

    private async Task Read(EntityUid user, Entity<WFOutpostComponent> outpost, int slot)
    {
        var owner = outpost.Comp.Owner;
        var message = "wf-outpost-load-failed";
        try
        {
            if (await _db.GetOutpostSaveDataAsync(owner, slot) is not { } row)
            {
                message = "wf-outpost-load-empty";
                return;
            }

            if (IsInUse(row.Save))
            {
                message = "wf-outpost-load-in-use";
                return;
            }

            var yaml = await Task.Run(() => Unpack(row.Data));
            Stage(user, outpost, slot, row.Save.Name, yaml, out message);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load outpost save slot {slot} of {owner}: {e}");
            message = "wf-outpost-load-failed";
        }
        finally
        {
            _busy.Remove(owner);
            if (!TerminatingOrDeleted(user))
                _popup.PopupEntity(Loc.GetString(message), user, user);
            RefreshOwnerUi(owner);
        }
    }

    /// <summary>
    /// Loads a save onto the staging map, prices it (a preset at its own <paramref name="price"/>), charges the user and
    /// draws its footprint on the ground.
    /// </summary>
    private bool Stage(EntityUid user, Entity<WFOutpostComponent> outpost, int? slot, string name, string yaml, out string message,
        int? price = null)
    {
        if (TerminatingOrDeleted(outpost) || TerminatingOrDeleted(user) || _loads.ContainsKey(outpost)
            || !TryComp<MapGridComponent>(outpost, out var liveGrid))
        {
            message = "wf-outpost-load-lost";
            return false;
        }

        if (!IsFresh(outpost, out message))
            return false;

        if (!TryStageSave(yaml, $"outpost save {name}", out var loaded, out var stagedConsole))
        {
            Log.Error($"Outpost {(slot is { } s ? $"save slot {s}" : $"preset {name}")} of {outpost.Comp.Owner} failed to stage.");
            message = "wf-outpost-load-failed";
            return false;
        }

        var shift = _maps.TileIndicesFor(outpost, liveGrid, Transform(outpost.Comp.Console).Coordinates)
                    - _maps.TileIndicesFor(loaded.Owner, loaded.Comp, Transform(stagedConsole).Coordinates);
        var load = new PendingLoad
        {
            Owner = outpost.Comp.Owner,
            Slot = slot,
            Name = name,
            Price = price ?? LoadPrice(LoadAppraisal(loaded)),
            Staged = loaded.Owner,
            StagedConsole = stagedConsole,
            Shift = shift,
            Lands = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(OutpostCVars.LoadDelay)),
        };

        var footprint = GetFootprint(load);
        if (!IsClearFor((outpost, outpost.Comp, liveGrid), footprint, out message))
        {
            Del(loaded.Owner);
            return false;
        }

        if (load.Price > 0 && !_bank.TryBankWithdraw(user, load.Price))
        {
            Del(loaded.Owner);
            message = "wf-outpost-load-cannot-afford";
            return false;
        }

        _loads[outpost] = load;
        var pending = EnsureComp<WFOutpostPendingLoadComponent>(outpost);
        pending.Tiles = footprint;
        pending.Lands = load.Lands;
        Dirty(outpost, pending);

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} started loading outpost {name} ({(slot is { } s2 ? $"slot {s2}" : "preset")}) onto {ToPrettyString(outpost):grid} for {load.Price}");
        message = "wf-outpost-load-started";
        return true;
    }

    /// <summary>Loads a save onto the paused staging map, strips it and finds its console; false if either fails.</summary>
    private bool TryStageSave(string yaml, string label, out Entity<MapGridComponent> staged, out EntityUid console)
    {
        staged = default;
        console = default;
        if (!Exists(_stageMap))
        {
            _stageMap = _maps.CreateMap(out var stageId);
            _maps.SetPaused(stageId, true);
            _stageOffset = 0;
        }

        _stageOffset += StageSpacing;
        MappingDataNode data;
        using (var reader = new StringReader(yaml))
        {
            if (DataNodeParser.ParseYamlStream(reader).FirstOrDefault()?.Root is not MappingDataNode root)
                return false;
            data = root;
        }

        var depths = TakeDepths(data);
        var mapId = Transform(_stageMap.Value).MapID;
        if (!TryStageGrid(data, label, mapId, new Vector2(_stageOffset, 0f), out var loaded))
            return false;

        Strip(loaded.Value);
        if (!TryFindConsole(loaded.Value, out console))
        {
            Del(loaded.Value);
            return false;
        }

        staged = loaded.Value;
        // Each grid below stages beside the outpost and goes down under it when it settles.
        foreach (var (depth, gridData) in depths)
        {
            if (depth != -1 || EnsureComp<WFOutpostComponent>(staged).Basement != null
                || !TryStageGrid(gridData, $"{label} depth {depth}", mapId, new Vector2(_stageOffset, StageSpacing / 2), out var below))
                continue;

            Strip(below.Value);
            var basement = EnsureComp<WFOutpostBasementComponent>(below.Value);
            basement.Outpost = staged;
            Comp<WFOutpostComponent>(staged).Basement = below.Value;
        }

        return true;
    }

    /// <summary>Loads one grid of a save onto the staging map.</summary>
    private bool TryStageGrid(MappingDataNode data, string label, MapId mapId, Vector2 offset,
        [NotNullWhen(true)] out Entity<MapGridComponent>? grid)
    {
        grid = null;
        var options = new MapLoadOptions
        {
            MergeMap = mapId,
            Offset = offset,
            ExpectedCategory = FileCategory.Grid,
        };

        if (!_mapLoader.TryLoadGeneric(data, label, out var result, options))
            return false;

        if (result.Grids.Count != 1)
        {
            _mapLoader.Delete(result);
            return false;
        }

        grid = result.Grids.Single();
        return true;
    }

    /// <summary>Takes off the copy what a save never keeps (<see cref="IsKept"/>), wherever it sits.</summary>
    private void Strip(EntityUid grid)
    {
        Strip(grid, IsKept);
    }

    /// <summary>Takes off a loaded copy whatever <paramref name="kept"/> doesn't keep, wherever it sits.</summary>
    private void Strip(EntityUid grid, Func<EntityUid, bool> kept)
    {
        var strip = new List<EntityUid>();
        var pending = new Stack<EntityUid>();
        pending.Push(grid);
        while (pending.TryPop(out var uid))
        {
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (kept(child))
                    pending.Push(child);
                else
                    strip.Add(child);
            }
        }

        foreach (var uid in strip)
        {
            Del(uid);
        }

        RemComp<WFOutpostPendingLoadComponent>(grid);
    }

    private bool TryFindConsole(EntityUid grid, out EntityUid console)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!HasComp<WFOutpostConsoleComponent>(child))
                continue;

            console = child;
            return true;
        }

        console = default;
        return false;
    }

    /// <summary>The tiles a staged save will cover, in the founding outpost's tile coordinates.</summary>
    private List<Vector2i> GetFootprint(PendingLoad load)
    {
        var tiles = new List<Vector2i>();
        var grid = Comp<MapGridComponent>(load.Staged);
        var enumerator = _maps.GetAllTilesEnumerator(load.Staged, grid);
        while (enumerator.MoveNext(out var tile))
        {
            tiles.Add(tile.Value.GridIndices + load.Shift);
        }

        return tiles;
    }

    /// <summary>
    /// Whether a save may land over the given tiles of a founding outpost: open ground or the outpost's own, none of it
    /// in another's claim, and no reserved ground within the claim margin.
    /// </summary>
    private bool IsClearFor(Entity<WFOutpostComponent, MapGridComponent> outpost, List<Vector2i> tiles, out string reason)
    {
        var ground = outpost.Comp1.Ground;
        if (TerminatingOrDeleted(ground) || !TryComp<MapGridComponent>(ground, out var groundGrid))
        {
            reason = "wf-outpost-load-lost";
            return false;
        }

        var min = new Vector2i(int.MaxValue, int.MaxValue);
        var max = new Vector2i(int.MinValue, int.MinValue);
        foreach (var tile in tiles)
        {
            var world = _transform.ToMapCoordinates(new EntityCoordinates(outpost, tile + new Vector2(0.5f)));
            var index = _maps.TileIndicesFor(ground, groundGrid, world);
            min = Vector2i.ComponentMin(min, index);
            max = Vector2i.ComponentMax(max, index);

            if (TryGetClaim(ground, index, outpost.Comp1.Owner, out _))
            {
                reason = "wf-outpost-load-claimed";
                return false;
            }

            if (!outpost.Comp1.Carved.ContainsKey(index) && !IsOpenGround((ground, groundGrid), index, outpost, out reason))
                return false;
        }

        if (tiles.Count > 0 && IsNearReserved(ground, min, max))
        {
            reason = "wf-outpost-load-too-close";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public override void Update(float frameTime)
    {
        UpdateMarks();
        base.Update(frameTime);
        RestoreCaverns();
        UpdateAutosave();
        UpdateAccess();
        UpdateTrade();
        UpdateDrops();
        UpdateFoundations();
        if (_loads.Count == 0)
            return;

        var now = _timing.CurTime;
        List<EntityUid>? due = null;
        foreach (var (outpost, load) in _loads)
        {
            if (load.Lands <= now)
                (due ??= new()).Add(outpost);
        }

        if (due == null)
            return;

        foreach (var outpost in due)
        {
            Land(outpost);
        }
    }

    /// <summary>Swaps a staged save in for the founding outpost, or refunds it if its ground is no longer clear.</summary>
    private void Land(EntityUid liveUid)
    {
        if (!_loads.Remove(liveUid, out var load))
            return;

        RemComp<WFOutpostPendingLoadComponent>(liveUid);
        if (!TryComp<WFOutpostComponent>(liveUid, out var live) || !TryComp<MapGridComponent>(liveUid, out var liveGrid)
            || TerminatingOrDeleted(load.Staged) || TerminatingOrDeleted(load.StagedConsole))
        {
            Refund(load, null);
            return;
        }

        if (!IsClearFor((liveUid, live, liveGrid), GetFootprint(load), out var reason))
        {
            Log.Info($"Outpost save {load.Name} of {load.Owner} couldn't land on {ToPrettyString(liveUid)}: {reason}");
            Refund(load, "wf-outpost-load-blocked");
            RefreshOwnerUi(load.Owner);
            return;
        }

        var ground = live.Ground;
        var owner = live.Owner;
        var ownerName = live.OwnerName;
        var origin = Vector2.Transform((Vector2) load.Shift, _transform.GetWorldMatrix(liveUid));
        var rotation = _transform.GetWorldRotation(liveUid);

        // Whatever lies loose on the founding outpost stays where it is; the save picks it up as it lands.
        DropLoose((liveUid, live));

        // Deleting the founding outpost puts its ground back, so the save carves it again below.
        Del(liveUid);

        var grid = load.Staged;
        Settle(grid, load.StagedConsole, owner, ownerName, ground, load.Slot, load.Name, origin, rotation);
        Spawn(KitPrototype, Transform(load.StagedConsole).Coordinates);

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"Outpost save {load.Name} (slot {load.Slot}) of {owner} landed as {ToPrettyString(grid):grid} for {load.Price}");
        if (TryComp<WFOutpostConsoleComponent>(load.StagedConsole, out var console))
        {
            _popup.PopupEntity(Loc.GetString("wf-outpost-load-landed"), load.StagedConsole);
            UpdateUi((load.StagedConsole, console));
        }
    }

    /// <summary>
    /// Brings a staged save down at <paramref name="origin"/> on its ground's map as the owner's outpost: held down,
    /// carved in, its portals, access and slot set up, and the save marked loaded this round.
    /// </summary>
    private void Settle(EntityUid grid, EntityUid console, NetUserId owner, string ownerName, EntityUid ground, int? slot, string name,
        Vector2 origin, Angle rotation)
    {
        _transform.SetCoordinates(grid, new EntityCoordinates(_maps.GetMap(Transform(ground).MapID), Vector2.Zero));
        _transform.SetWorldPositionRotation(grid, origin, rotation);
        HoldDown(grid);

        var comp = EnsureComp<WFOutpostComponent>(grid);
        comp.Owner = owner;
        comp.OwnerName = ownerName;
        comp.Ground = ground;
        comp.Console = console;
        EnsureComp<WFOutpostConsoleComponent>(console).Outpost = grid;
        EnsureComp<CleanupImmuneComponent>(console);

        var gridComp = Comp<MapGridComponent>(grid);
        var all = new List<Vector2i>();
        var tiles = _maps.GetAllTilesEnumerator(grid, gridComp);
        while (tiles.MoveNext(out var tile))
        {
            all.Add(tile.Value.GridIndices);
        }

        CarveUnder((grid, comp, gridComp), all);
        RebuildPortals((grid, comp, gridComp));
        _reinit.ReinitLoadedShip(grid);
        comp.PendingAccess = new List<WFOutpostAccessGrant>(comp.Access);
        SetupAccess((grid, comp), null);
        SettleBasement((grid, comp, gridComp));
        var settled = new WFOutpostSettledEvent();
        RaiseLocalEvent(grid, ref settled);
        comp.Slot = slot;
        comp.SavedFingerprint = Fingerprint((grid, gridComp));
        comp.NextAutosave = _timing.CurTime + TimeSpan.FromSeconds(_cfg.GetCVar(OutpostCVars.AutosaveInterval));

        // A preset has no slot, and is saved into one only when its owner chooses; it takes the preset's name.
        if (slot is not { } index)
        {
            _metaData.SetEntityName(grid, name);
            return;
        }

        if (_slots.TryGetValue(owner, out var slots) && slots[index] is { } save)
            slots[index] = save with { LoadedRound = _ticker.RoundId };
        _ = MarkLoaded(owner, index, _ticker.RoundId);
    }

    /// <summary>Moves everything loose on an outpost (people and dropped things) onto the ground, where it stands.</summary>
    private void DropLoose(Entity<WFOutpostComponent> outpost)
    {
        var loose = new List<EntityUid>();
        var children = Transform(outpost).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (child != outpost.Comp.Console && !Transform(child).Anchored)
                loose.Add(child);
        }

        foreach (var ent in loose)
        {
            _transform.SetCoordinates(ent, new EntityCoordinates(outpost.Comp.Ground, _transform.GetWorldPosition(ent)));
        }
    }

    private async Task MarkLoaded(NetUserId owner, int slot, int round)
    {
        try
        {
            await _db.SetOutpostSaveLoadedAsync(owner, slot, round);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to mark outpost save slot {slot} of {owner} as loaded in round {round}: {e}");
        }
    }

    /// <summary>Calls off a load on its way down onto an outpost and refunds it, telling the owner why.</summary>
    public void CancelLoad(EntityUid outpost, string reason)
    {
        if (!_loads.Remove(outpost, out var load))
            return;

        if (!TerminatingOrDeleted(outpost))
            RemComp<WFOutpostPendingLoadComponent>(outpost);
        Refund(load, reason);
        RefreshOwnerUi(load.Owner);
    }

    private void Refund(PendingLoad load, string? reason)
    {
        if (!TerminatingOrDeleted(load.Staged))
            Del(load.Staged);

        EntityUid? body = _players.TryGetSessionById(load.Owner, out var session) ? session.AttachedEntity : null;
        if (load.Price > 0 && (body == null || !_bank.TryBankDeposit(body.Value, load.Price, tax: false)))
            Log.Error($"Could not refund {load.Price} to {load.Owner} for the outpost save {load.Name}.");

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"Outpost save {load.Name} (slot {load.Slot}) of {load.Owner} was called off and {load.Price} refunded");
        if (reason != null && body != null)
            _popup.PopupEntity(Loc.GetString(reason), body.Value, body.Value);
    }

    private void OnLoadMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostLoadMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost))
            return;

        if (!TryStartLoad(args.Actor, (grid, outpost), args.Slot, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), args.Actor, args.Actor);
        UpdateUi(console);
    }

    private void OnLoadPresetMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostLoadPresetMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost))
            return;

        // Started or not, the reason says which.
        TryStartPresetLoad(args.Actor, (grid, outpost), args.Preset, out var reason);
        _popup.PopupEntity(Loc.GetString(reason), args.Actor, args.Actor);
        UpdateUi(console);
    }

    private void OnCancelLoadMessage(Entity<WFOutpostConsoleComponent> console, ref WFOutpostCancelLoadMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !TryComp<ActorComponent>(args.Actor, out var actor) || actor.PlayerSession.UserId != outpost.Owner)
            return;

        CancelLoad(grid, "wf-outpost-load-cancelled");
        UpdateUi(console);
    }
}
