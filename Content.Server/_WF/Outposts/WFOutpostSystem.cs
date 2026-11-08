using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.OutpostRaids;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server.Administration.Logs;
using Content.Server.Parallax;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.Planets;
using Content.Shared.Database;
using Content.Shared.Interaction;
using Content.Shared.Light.Components;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Founds outposts from outpost console kits on natural planet ground, holds them down while their console stands,
/// and lets the owner name them.
/// </summary>
public sealed partial class WFOutpostSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WFRaidWindowSystem _raidWindow = default!;

    private List<Entity<MapGridComponent>> _grids = new();
    private readonly HashSet<EntityUid> _loose = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFOutpostKitComponent, AfterInteractEvent>(OnKitAfterInteract);
        SubscribeLocalEvent<WFOutpostConsoleComponent, ComponentShutdown>(OnConsoleShutdown);
        SubscribeLocalEvent<WFOutpostComponent, ComponentShutdown>(OnOutpostShutdown);
        InitializeRoofs();
        InitializeFoundation();
        InitializeClaims();
        InitializeFoundations();
        InitializeLoading();
        InitializeEditor();
        InitializePreview();
        InitializeAccess();
        InitializeDeploy();
        InitializeSale();
        InitializeSettlers();
        InitializeBasements();

        Subs.BuiEvents<WFOutpostConsoleComponent>(WFOutpostConsoleUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WFOutpostRenameMessage>(OnRename);
            subs.Event<WFOutpostSaveMessage>(OnSaveMessage);
            subs.Event<WFOutpostDeleteSaveMessage>(OnDeleteSaveMessage);
            subs.Event<WFOutpostLoadMessage>(OnLoadMessage);
            subs.Event<WFOutpostLoadPresetMessage>(OnLoadPresetMessage);
            subs.Event<WFOutpostCancelLoadMessage>(OnCancelLoadMessage);
            subs.Event<WFOutpostOrderMessage>(OnOrderMessage);
            subs.Event<WFOutpostSellMessage>(OnSellMessage);
            subs.Event<WFOutpostSellAtRoundEndMessage>(OnSellAtRoundEndMessage);
            subs.Event<WFOutpostOfferMessage>(OnOfferMessage);
            subs.Event<WFOutpostWithdrawOfferMessage>(OnWithdrawOfferMessage);
            subs.Event<WFOutpostRefreshBuyersMessage>(OnRefreshBuyersMessage);
            _shipAccess.SubscribeAccessTab(subs);
        });
    }

    private void OnKitAfterInteract(Entity<WFOutpostKitComponent> kit, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        args.Handled = true;
        if (!TryFound(args.User, kit, args.ClickLocation, out _, out var reason))
            _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
    }

    /// <summary>The live outpost an account owns, if it has one.</summary>
    public bool TryGetOutpost(NetUserId owner, out Entity<WFOutpostComponent> outpost)
    {
        var query = EntityQueryEnumerator<WFOutpostComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Owner != owner || comp.Abandoned || TerminatingOrDeleted(uid))
                continue;

            outpost = (uid, comp);
            return true;
        }

        outpost = default;
        return false;
    }

    /// <summary>
    /// Founds an outpost centred on <paramref name="target"/>: a square grid anchored over natural ground, with the
    /// kit's console in the middle. The kit is used up. <paramref name="reason"/> is a loc id when it fails.
    /// </summary>
    public bool TryFound(
        EntityUid user,
        Entity<WFOutpostKitComponent> kit,
        EntityCoordinates target,
        [NotNullWhen(true)] out EntityUid? outpost,
        out string reason)
    {
        outpost = null;
        if (!_cfg.GetCVar(OutpostCVars.Enabled))
        {
            reason = "wf-outpost-found-disabled";
            return false;
        }

        if (!TryComp<ActorComponent>(user, out var actor))
        {
            reason = "wf-outpost-found-no-owner";
            return false;
        }

        var owner = actor.PlayerSession.UserId;
        if (TryGetOutpost(owner, out _))
        {
            reason = "wf-outpost-found-already-own";
            return false;
        }

        if (TryGetOutpostTile(_transform.ToMapCoordinates(target), out var abandoned, out var abandonedTile) && abandoned.Comp1.Abandoned)
            return TryRefound(user, owner, kit, abandoned, abandonedTile, out outpost, out reason);

        if (!TryGetFootprint(kit.Comp, target, out var ground, out var origin, out reason)
            || !IsClaimable((ground, Comp<MapGridComponent>(ground)), origin, kit.Comp.Size, out reason))
            return false;

        var mapId = Transform(ground).MapID;
        var grid = _maps.CreateGridEntity(mapId);
        _transform.SetWorldPositionRotation(grid.Owner,
            Vector2.Transform(origin, _transform.GetWorldMatrix(ground)),
            _transform.GetWorldRotation(ground));

        var floor = new Tile(_tiles[kit.Comp.Tile].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < kit.Comp.Size; x++)
        for (var y = 0; y < kit.Comp.Size; y++)
            tiles.Add((new Vector2i(x, y), floor));
        _maps.SetTiles(grid.Owner, grid.Comp, tiles);

        HoldDown(grid);

        var ownerName = MetaData(user).EntityName;
        var comp = EnsureComp<WFOutpostComponent>(grid);
        comp.Owner = owner;
        comp.OwnerName = ownerName;
        comp.Ground = ground;
        comp.FoundingTiles = kit.Comp.Size * kit.Comp.Size;
        comp.Lineage = Guid.NewGuid().ToString();
        _metaData.SetEntityName(grid, Loc.GetString("wf-outpost-default-name", ("owner", ownerName)));

        var middle = kit.Comp.Size / 2 + 0.5f;
        var console = Spawn(kit.Comp.Console, new EntityCoordinates(grid, middle, middle));
        EnsureComp<CleanupImmuneComponent>(console);
        EnsureComp<WFOutpostConsoleComponent>(console).Outpost = grid;
        comp.Console = console;

        var all = new List<Vector2i>();
        for (var x = 0; x < kit.Comp.Size; x++)
        for (var y = 0; y < kit.Comp.Size; y++)
            all.Add(new Vector2i(x, y));
        CarveUnder((grid, comp, grid.Comp), all);
        RebuildPortals((grid, comp, grid.Comp));
        SetupAccess((grid, comp), user);
        QueueDel(kit);

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{ToPrettyString(user):user} founded outpost {ToPrettyString(grid):grid} at {target}");
        _popup.PopupEntity(Loc.GetString("wf-outpost-found-done"), user, user);

        outpost = grid;
        reason = string.Empty;
        return true;
    }

    /// <summary>Holds a grid down as an outpost: anchored, static, kept from cleanup, open to the sky until roofed.</summary>
    private void HoldDown(EntityUid grid)
    {
        // The grid is created or loaded on a running map, so ForceAnchor's own map-init hold never runs; hold it here.
        EnsureComp<ForceAnchorComponent>(grid);
        EnsureComp<PreventGridAnchorChangesComponent>(grid);
        _shuttle.Disable(grid, force: true);
        EnsureComp<CleanupImmuneComponent>(grid);
        // Its floor may lie in separate patches across the claim, and a split would tear them off the outpost.
        Comp<MapGridComponent>(grid).CanSplit = false;
        // Open to the sky until roofed (spike F0.3): explicit roofs instead of a vessel's implied one.
        RemComp<ImplicitRoofComponent>(grid);
        EnsureComp<RoofComponent>(grid);
        EnsureComp<WFPlanetTerrainGridComponent>(grid);
    }

    /// <summary>
    /// The ground map under <paramref name="target"/> and the corner tile of the square centred on it, when every tile
    /// of that square is natural planet ground with nothing built, anchored or landed on it.
    /// </summary>
    private bool TryGetFootprint(WFOutpostKitComponent kit, EntityCoordinates target, out EntityUid ground, out Vector2i origin, out string reason)
    {
        ground = default;
        origin = default;
        var map = _transform.GetMap(target);
        if (map is not { } mapUid || !IsGroundLayer(mapUid) || !TryComp<MapGridComponent>(mapUid, out var groundGrid))
        {
            reason = "wf-outpost-found-not-ground";
            return false;
        }

        ground = mapUid;
        var centre = _maps.TileIndicesFor(mapUid, groundGrid, _transform.ToMapCoordinates(target));
        origin = centre - new Vector2i(kit.Size / 2, kit.Size / 2);

        for (var x = 0; x < kit.Size; x++)
        for (var y = 0; y < kit.Size; y++)
        {
            if (!IsOpenGround((mapUid, groundGrid), origin + new Vector2i(x, y), null, out reason))
                return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Whether an outpost may be laid over a ground tile: natural ground, not built over, with nothing anchored on it but
    /// the biome's own growth and no grid over it other than <paramref name="ignore"/>.
    /// </summary>
    private bool IsOpenGround(Entity<MapGridComponent> ground, Vector2i index, EntityUid? ignore, out string reason)
    {
        var built = CompOrNull<WFPlanetBuiltTilesComponent>(ground);
        if (!_maps.TryGetTileRef(ground, ground.Comp, index, out var tileRef) || tileRef.Tile.IsEmpty
            || built != null && built.Underlay.ContainsKey(index))
        {
            reason = "wf-outpost-found-not-natural";
            return false;
        }

        if (IsReserved(ground, index))
        {
            reason = "wf-outpost-found-reserved";
            return false;
        }

        var anchored = _maps.GetAnchoredEntitiesEnumerator(ground, ground.Comp, index);
        while (anchored.MoveNext(out var ent))
        {
            if (!_biome.WfIsBiomeAnchored(ground, ent.Value, index))
            {
                reason = "wf-outpost-found-blocked";
                return false;
            }
        }

        var box = new Box2(index, index + Vector2i.One).Enlarged(-0.05f);
        _grids.Clear();
        _maps.FindGridsIntersecting(Transform(ground).MapID, _transform.GetWorldMatrix(ground).TransformBox(box), ref _grids,
            approx: false, includeMap: false);
        foreach (var grid in _grids)
        {
            if (grid.Owner != ignore)
            {
                reason = "wf-outpost-found-occupied";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Carves the ground out from under the given outpost tiles and moves whatever lay loose on it onto the outpost.
    /// </summary>
    private void CarveUnder(Entity<WFOutpostComponent, MapGridComponent> outpost, List<Vector2i> outpostTiles)
    {
        if (!TryComp<MapGridComponent>(outpost.Comp1.Ground, out var groundGrid))
            return;

        var ground = outpost.Comp1.Ground;
        var indices = new List<Vector2i>();
        foreach (var tile in outpostTiles)
        {
            var world = _transform.ToMapCoordinates(new EntityCoordinates(outpost, tile + new Vector2(0.5f)));
            var index = _maps.TileIndicesFor(ground, groundGrid, world);
            indices.Add(index);

            _loose.Clear();
            _lookup.GetLocalEntitiesIntersecting(ground, index, _loose, flags: LookupFlags.Dynamic | LookupFlags.Sundries);
            foreach (var ent in _loose)
            {
                var xform = Transform(ent);
                if (xform.ParentUid == ground && !xform.Anchored)
                    _transform.SetParent(ent, xform, outpost);
            }
        }

        _biome.WfCarve((ground, groundGrid), indices, outpost.Comp1.Carved);
    }

    /// <summary>Whether a map is the ground of a planet network, not its orbit, air layers or caverns.</summary>
    private bool IsGroundLayer(EntityUid map)
    {
        if (!HasComp<WFPlanetLayerComponent>(map))
            return false;

        var query = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (query.MoveNext(out var network))
        {
            if (network.GroundMap == map)
                return true;
        }

        return false;
    }

    private void OnConsoleShutdown(Entity<WFOutpostConsoleComponent> console, ref ComponentShutdown args)
    {
        if (console.Comp.Outpost is { } grid && TryComp<WFOutpostComponent>(grid, out var outpost) && outpost.Console == console.Owner)
            Abandon((grid, outpost));
    }

    private void OnOutpostShutdown(Entity<WFOutpostComponent> outpost, ref ComponentShutdown args)
    {
        CancelLoad(outpost, "wf-outpost-load-lost");
        RemoveBasement(outpost);
        RemovePortals(outpost.Comp);
        // Whether released or deleted, the outpost leaves no hole in the ground.
        if (!TerminatingOrDeleted(outpost.Comp.Ground) && TryComp<MapGridComponent>(outpost.Comp.Ground, out var groundGrid))
            _biome.WfRestoreCarved((outpost.Comp.Ground, groundGrid), outpost.Comp.Carved);
    }

    /// <summary>
    /// Abandons an outpost that lost its console: it stays held down and keeps its claim, but isn't saved or sold, and its
    /// owner may found another. Only they can found it again, with a console kit on its floor.
    /// </summary>
    public void Abandon(Entity<WFOutpostComponent> outpost)
    {
        if (TerminatingOrDeleted(outpost))
            return;

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"Outpost {ToPrettyString(outpost):grid} lost its console and was abandoned");
        CancelLoad(outpost, "wf-outpost-load-lost");
        outpost.Comp.Abandoned = true;
        outpost.Comp.Console = EntityUid.Invalid;
        outpost.Comp.Offer = null;
        outpost.Comp.SellAtRoundEnd = false;
    }

    /// <summary>
    /// Founds an abandoned outpost again for its owner: the kit's console goes on the clear floor tile it is used on.
    /// </summary>
    private bool TryRefound(
        EntityUid user,
        NetUserId owner,
        Entity<WFOutpostKitComponent> kit,
        Entity<WFOutpostComponent, MapGridComponent> outpost,
        Vector2i index,
        [NotNullWhen(true)] out EntityUid? refounded,
        out string reason)
    {
        refounded = null;
        if (outpost.Comp1.Owner != owner)
        {
            reason = "wf-outpost-refound-not-yours";
            return false;
        }

        if (!_maps.TryGetTileRef(outpost, outpost.Comp2, index, out var tile) || _turf.IsTileBlocked(tile, DeployBlockers))
        {
            reason = "wf-outpost-deploy-blocked";
            return false;
        }

        var console = Spawn(kit.Comp.Console, new EntityCoordinates(outpost, index + new Vector2(0.5f)));
        EnsureComp<CleanupImmuneComponent>(console);
        EnsureComp<WFOutpostConsoleComponent>(console).Outpost = outpost;
        outpost.Comp1.Console = console;
        outpost.Comp1.Abandoned = false;
        QueueDel(kit);

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{ToPrettyString(user):user} founded abandoned outpost {ToPrettyString(outpost):grid} again");
        _popup.PopupEntity(Loc.GetString("wf-outpost-refound-done"), user, user);
        refounded = outpost;
        reason = string.Empty;
        return true;
    }

    private void OnUiOpened(Entity<WFOutpostConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        UpdateUi(console);
    }

    private void OnRename(Entity<WFOutpostConsoleComponent> console, ref WFOutpostRenameMessage args)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !TryComp<ActorComponent>(args.Actor, out var actor))
            return;

        if (!TrySetName((grid, outpost), actor.PlayerSession.UserId, args.Name))
            return;

        _adminLog.Add(LogType.WFOutpost, LogImpact.Low, $"{ToPrettyString(args.Actor):user} renamed outpost {ToPrettyString(grid):grid}");
        UpdateUi(console);
    }

    /// <summary>Renames an outpost for its owner, trimmed to the name limit. False for anyone else or an empty name.</summary>
    public bool TrySetName(Entity<WFOutpostComponent> outpost, NetUserId user, string name)
    {
        if (user != outpost.Comp.Owner)
            return false;

        name = name.Trim();
        if (name.Length > WFOutpostLimits.NameLength)
            name = name[..WFOutpostLimits.NameLength].TrimEnd();
        if (name.Length == 0)
            return false;

        _metaData.SetEntityName(outpost, name);
        return true;
    }

    private void UpdateUi(Entity<WFOutpostConsoleComponent> console)
    {
        if (console.Comp.Outpost is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        WFOutpostPendingLoad? pending = null;
        if (_loads.TryGetValue(grid, out var load))
            pending = new WFOutpostPendingLoad(load.Name, load.Price, load.Lands);

        var state = new WFOutpostConsoleState(Name(grid), outpost.OwnerName, outpost.Owner,
            CountAllTiles((grid, outpost, gridComp)), _cfg.GetCVar(OutpostCVars.MaxTiles),
            CountAllEntities((grid, outpost)), _cfg.GetCVar(OutpostCVars.MaxEntities),
            GetSlotViews(outpost.Owner), IsBusy(outpost.Owner), pending, GetTradeView((grid, outpost)),
            GetSaleView(console, (grid, outpost, gridComp)));
        if (Transform(grid).MapUid is { } map && _raidWindow.TryGetWindowOn(map, out var active, out var until))
            state.Raid = new WFOutpostRaidView(active, until > TimeSpan.Zero ? _timing.CurTime + until : null);
        _ui.SetUiState(console.Owner, WFOutpostConsoleUiKey.Key, state);
    }

    /// <summary>Floor tiles on an outpost.</summary>
    public int CountTiles(Entity<MapGridComponent> grid)
    {
        var tiles = 0;
        var enumerator = _maps.GetAllTilesEnumerator(grid, grid.Comp);
        while (enumerator.MoveNext(out _))
            tiles++;
        return tiles;
    }

    /// <summary>Everything on an outpost, contents of containers included: what a save would hold.</summary>
    public int CountEntities(EntityUid grid)
    {
        var count = 0;
        var stack = new Stack<EntityUid>();
        stack.Push(grid);
        while (stack.TryPop(out var uid))
        {
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                count++;
                stack.Push(child);
            }
        }

        return count;
    }
}
