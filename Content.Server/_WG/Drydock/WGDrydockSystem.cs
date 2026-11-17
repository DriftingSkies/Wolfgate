using Content.Server._WG.ShipCommand;
using Content.Shared._WG.ShipCommand;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._WG.Planets.Flight;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Destructible;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.ShipRepair;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Drydock;

/// <summary>
/// Drydock berth console: sells repairs and refits for the ship held in its berth area.
/// </summary>
public sealed partial class WGDrydockSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private Lattice.WGLatticeSystem _lattice = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private WGShipRefitSystem _refits = default!;
    [Dependency] private WGHullSectionSystem _hull = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private WGTradeMarketSystem _tradeMarket = default!;
    [Dependency] private WGShipServiceSystem _shipServices = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    /// <summary>Upper bound on ship tiles kept for ambient sparks.</summary>
    private const int MaxSparkTiles = 512;

    private List<Entity<MapGridComponent>> _grids = new();

    private TimeSpan _nextConsoleRefresh;

    /// <summary>Cached estimated price and construction material value per entity prototype.</summary>
    private readonly Dictionary<string, (double Estimate, double Materials)> _estimates = new();

    public override void Initialize()
    {
        base.Initialize();
        InitializeVouchers();

        SubscribeLocalEvent<WGDrydockConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<WGDrydockConsoleComponent, WGDrydockQuoteRequestMessage>(OnQuoteRequest);
        SubscribeLocalEvent<WGDrydockConsoleComponent, WGDrydockRepairMessage>(OnRepair);
        SubscribeLocalEvent<WGDrydockConsoleComponent, WGDrydockUpgradeMessage>(OnUpgrade);
        SubscribeLocalEvent<WGDrydockConsoleComponent, WGDrydockCleanDriftMessage>(OnCleanDrift);
        SubscribeLocalEvent<WGDrydockCraneComponent, MapInitEvent>(OnCraneMapInit);
        SubscribeLocalEvent<WGDrydockCraneComponent, ComponentShutdown>(OnCraneShutdown);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ =>
        {
            _estimates.Clear();
            _constructionMaterials.Clear();
        });

        InitializeBerthLights();
        InitializeBooking();
        InitializeBreak();
        InitializeLinking();
        InitializeUpgrades();
        InitializeTerminal();
    }

    /// <summary>
    /// The ship grid with the most tiles in the console's berth area, or null. Any tile counts, so a hull bigger than the
    /// berth, or with its centre out of the cranes' reach, is still found.
    /// </summary>
    public EntityUid? GetBerthedShip(EntityUid console, WGDrydockConsoleComponent comp)
    {
        if (!TryGetBerth(console, comp, out var berth))
            return null;

        var stationXform = Transform(berth.Grid);
        var worldAabb = _transform.GetWorldMatrix(stationXform).TransformBox(berth.Area);
        var (stationPos, stationRot) = _transform.GetWorldPositionRotation(stationXform);
        var worldArea = new Box2Rotated(berth.Area.Translated(stationPos), stationRot, stationPos);

        _grids.Clear();
        _map.FindGridsIntersecting(stationXform.MapID, worldAabb, ref _grids);

        EntityUid? best = null;
        var bestTiles = 0;
        foreach (var grid in _grids)
        {
            if (grid.Owner == berth.Grid || !HasComp<ShuttleComponent>(grid) || HasComp<MapComponent>(grid))
                continue;

            var tiles = _map.GetTilesIntersecting(grid, grid.Comp, worldArea).Count();
            if (tiles <= bestTiles)
                continue;

            best = grid;
            bestTiles = tiles;
        }

        return best;
    }

    /// <summary>Whether the centre of a ship's bounds lies in the console's berth area.</summary>
    private bool CentredInBerth(EntityUid console, WGDrydockConsoleComponent comp, EntityUid ship)
    {
        if (!TryGetBerth(console, comp, out var berth) || !TryComp<MapGridComponent>(ship, out var grid))
            return false;

        var world = Vector2.Transform(grid.LocalAABB.Center, _transform.GetWorldMatrix(ship));
        return berth.Area.Contains(Vector2.Transform(world, _transform.GetInvWorldMatrix(berth.Grid)));
    }

    /// <summary>Resolves a console's berth from its linked cranes: the area covering their reach.</summary>
    public bool TryGetBerth(EntityUid console, WGDrydockConsoleComponent comp, out WGDrydockBerth berth)
    {
        berth = default;
        if (Transform(console).GridUid is not { } grid)
            return false;

        var cranes = LinkedCranes(console);
        if (cranes.Count == 0)
            return false;

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        var arms = Vector2.Zero;
        foreach (var uid in cranes)
        {
            var crane = Comp<WGDrydockCraneComponent>(uid);
            var xform = Transform(uid);
            var rotation = Matrix3x2.CreateRotation((float) xform.LocalRotation.Theta);
            var toGrid = rotation * Matrix3x2.CreateTranslation(xform.LocalPosition);

            var a = crane.ArmToLocal(0f, crane.BerthGap);
            var b = crane.ArmToLocal(crane.MaxExtension, crane.MaxArm);
            var corners = new[] { new Vector2(a.X, a.Y), new Vector2(b.X, a.Y), new Vector2(a.X, b.Y), new Vector2(b.X, b.Y) };
            foreach (var corner in corners)
            {
                var point = Vector2.Transform(corner, toGrid);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            arms += Vector2.Transform(new Vector2(0, -1), rotation);
        }

        // Ships come in along the arms' average direction, snapped to the nearest grid axis.
        var approach = arms.LengthSquared() < 1e-6f
            ? new Vector2(0, -1)
            : Math.Abs(arms.X) >= Math.Abs(arms.Y)
                ? new Vector2(MathF.Sign(arms.X), 0)
                : new Vector2(0, MathF.Sign(arms.Y));

        // The area covers every linked crane; only unblocked cranes take jobs.
        cranes.RemoveAll(uid => Comp<WGDrydockCraneComponent>(uid).Blocked);
        var label = !string.IsNullOrEmpty(comp.Berth) ? comp.Berth : MetaData(console).EntityName;
        berth = new WGDrydockBerth(grid, label, new Box2(min, max), approach, cranes);
        return true;
    }

    /// <summary>The cranes linked to a console that stand on its grid, in entity order.</summary>
    public List<EntityUid> LinkedCranes(EntityUid console)
    {
        var result = new List<EntityUid>();
        if (Transform(console).GridUid is not { } grid)
            return result;

        var query = EntityQueryEnumerator<WGDrydockCraneComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crane, out var xform))
        {
            if (crane.LinkedConsole == console && xform.GridUid == grid)
                result.Add(uid);
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    /// <summary>The name a console's berth goes by in messages.</summary>
    private string BerthLabel(EntityUid console, WGDrydockConsoleComponent comp)
    {
        return TryGetBerth(console, comp, out var berth) ? berth.Label : comp.Berth;
    }

    /// <summary>Lists the work needed to restore the ship: hull tiles, missing entities, heals, then owned refits.</summary>
    public List<WGDrydockWorkItem> BuildRepairItems(EntityUid ship, WGDrydockConsoleComponent comp)
    {
        var items = new List<WGDrydockWorkItem>();
        if (!TryComp<ShipRepairDataComponent>(ship, out var data) || !TryComp<MapGridComponent>(ship, out var grid))
            return items;

        AddMissingTiles(ship, grid, data, comp, items);
        AddMissingEntities(ship, data, comp, items);
        AddHeals(ship, grid, comp, items);
        AddRefitRepairs(ship, comp, items);
        CapRepair(ship, comp, items);
        return items;
    }

    /// <summary>
    /// The same work unpriced and uncapped, for staff: hull tiles and missing entities when the ship has a repair
    /// snapshot, and heals either way. Owned refits are reapplied as each entity is rebuilt.
    /// </summary>
    public List<WGDrydockWorkItem> BuildFreeRepairItems(EntityUid ship)
    {
        var items = new List<WGDrydockWorkItem>();
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return items;

        if (TryComp<ShipRepairDataComponent>(ship, out var data))
        {
            AddMissingTiles(ship, grid, data, null, items);
            AddMissingEntities(ship, data, null, items);
        }

        AddHeals(ship, grid, null, items);
        return items;
    }

    /// <summary>
    /// The ship's value for fees and caps: its shipyard price when it was bought at one, else the price of rebuilding
    /// it whole from its repair snapshot. Vessel is false for the rebuild price; (0, false) without a snapshot.
    /// </summary>
    public (int Value, bool Vessel) ShipValue(EntityUid ship, WGDrydockConsoleComponent comp)
    {
        if (TryComp<VesselComponent>(ship, out var vessel) &&
            _prototype.TryIndex<VesselPrototype>(vessel.VesselId, out var proto) && proto.Price > 0)
        {
            return (proto.Price, true);
        }

        if (!TryComp<ShipRepairDataComponent>(ship, out var data))
            return (0, false);

        var value = 0d;
        foreach (var chunk in data.Chunks.Values)
        {
            foreach (var tile in chunk.Tiles)
            {
                if (tile != Tile.Empty.TypeId)
                    value += comp.TilePrice;
            }

            foreach (var spec in chunk.Entities.Values)
            {
                value += RebuildPrice(comp, _prototype.Index<EntityPrototype>(data.EntityPalette[spec.ProtoIndex].Id));
            }
        }

        return ((int) Math.Min(value, int.MaxValue), false);
    }

    /// <summary>The repair fee: a share of the ship's value, at least the console's minimum.</summary>
    public int RepairFee(EntityUid ship, WGDrydockConsoleComponent comp)
    {
        var (value, _) = ShipValue(ship, comp);
        return Math.Max(comp.MinFee, (int) Math.Round(value * comp.FeeFraction / 10) * 10);
    }

    /// <summary>Scales items down so the repair costs no more than its cap. Refit shares are charged in full, outside it.</summary>
    private void CapRepair(EntityUid ship, WGDrydockConsoleComponent comp, List<WGDrydockWorkItem> items)
    {
        var (value, vessel) = ShipValue(ship, comp);
        var cap = (long) (value * (vessel ? comp.CapFraction : comp.SnapshotCapFraction));
        var total = items.Sum(i => (long) (i.Cost - i.RefitCost));
        if (cap <= 0 || total <= cap)
            return;

        var scale = (double) cap / total;
        foreach (var item in items)
        {
            var capped = item.Cost - item.RefitCost;
            if (capped <= 0)
                continue;

            item.Cost = Math.Max(1, (int) (capped * scale)) + item.RefitCost;
            item.Share *= (float) scale;
        }
    }

    /// <summary>Adds a tile item for each stored hull tile the ship no longer has.</summary>
    private void AddMissingTiles(EntityUid ship, MapGridComponent grid, ShipRepairDataComponent data, WGDrydockConsoleComponent? comp, List<WGDrydockWorkItem> items)
    {
        var size = data.ChunkSize;
        foreach (var (chunkPos, chunk) in data.Chunks)
        {
            for (var i = 0; i < chunk.Tiles.Length; i++)
            {
                var stored = chunk.Tiles[i];
                if (stored == Tile.Empty.TypeId)
                    continue;

                var index = new Vector2i(chunkPos.X * size + i % size, chunkPos.Y * size + i / size);
                var current = _map.GetTileRef(ship, grid, index).Tile.TypeId;

                // Free work only re-lays hull that is gone or stripped to its sub-floor; a floor laid in its place stays.
                if (current == stored || (comp == null && !IsRemnant(current, stored)))
                    continue;

                items.Add(new WGDrydockWorkItem
                {
                    Kind = WGDrydockWorkKind.Tile,
                    TileIndex = index,
                    TileType = stored,
                    Cost = comp?.TilePrice ?? 0,
                    LocalPos = new Vector2(index.X + 0.5f, index.Y + 0.5f),
                });
            }
        }
    }

    /// <summary>
    /// Whether a free tile item still has hull to lay: the tile there is gone or down to one of its base turfs. False
    /// once it is laid, or once another floor was laid there after the item was listed.
    /// </summary>
    public bool IsHullMissing(EntityUid ship, WGDrydockWorkItem item)
    {
        return TryComp<MapGridComponent>(ship, out var grid)
               && IsRemnant(_map.GetTileRef(ship, grid, item.TileIndex).Tile.TypeId, item.TileType);
    }

    /// <summary>Whether a tile is what is left of another once it is pried up or blown down: nothing, or one of its base turfs.</summary>
    private bool IsRemnant(int current, int stored)
    {
        if (current == Tile.Empty.TypeId)
            return true;

        // Base turfs end at space within a few steps; the cap only guards against a looping definition.
        var def = _tileDefs[stored] as ContentTileDefinition;
        for (var depth = 0; depth < 8 && def != null && !string.IsNullOrEmpty(def.BaseTurf); depth++)
        {
            def = _tileDefs[def.BaseTurf] as ContentTileDefinition;
            if (def?.TileId == current)
                return true;
        }

        return false;
    }

    /// <summary>Adds a rebuild item for each snapshot entity the ship is missing.</summary>
    private void AddMissingEntities(EntityUid ship, ShipRepairDataComponent data, WGDrydockConsoleComponent? comp, List<WGDrydockWorkItem> items)
    {
        foreach (var (chunkPos, chunk) in data.Chunks)
        {
            foreach (var (specId, spec) in chunk.Entities)
            {
                if (!IsMissing(ship, spec) || IsSpotTaken(ship, data, chunk, spec))
                    continue;

                var proto = _prototype.Index<EntityPrototype>(data.EntityPalette[spec.ProtoIndex].Id);
                items.Add(new WGDrydockWorkItem
                {
                    Kind = WGDrydockWorkKind.Entity,
                    ChunkPos = chunkPos,
                    SpecId = specId,
                    Cost = comp != null ? RebuildPrice(comp, proto) : 0,
                    Prototype = proto.ID,
                    Mounted = proto.TryComp<HardpointAnchorableOnlyComponent>(out _, Factory),
                    LocalPos = spec.LocalPosition,
                });
            }
        }
    }

    /// <summary>Adds a heal item for each anchored part of the ship that has damage.</summary>
    private void AddHeals(EntityUid ship, MapGridComponent grid, WGDrydockConsoleComponent? comp, List<WGDrydockWorkItem> items)
    {
        var damaged = new HashSet<Entity<DamageableComponent>>();
        _lookup.GetLocalEntitiesIntersecting(ship, grid.LocalAABB, damaged);
        foreach (var ent in damaged)
        {
            if (TerminatingOrDeleted(ent))
                continue;

            var xform = Transform(ent);
            if (xform.ParentUid != ship || !xform.Anchored || HasComp<MobStateComponent>(ent))
                continue;

            var total = ent.Comp.TotalDamage;
            var faulted = HasComp<WGCrashApcFaultComponent>(ent);
            if (total <= FixedPoint2.Zero && !faulted)
                continue;

            // Patching a part costs its rebuild price times the share of its health lost, never more than rebuilding it.
            var proto = MetaData(ent).EntityPrototype;
            int? rebuild = proto != null && comp != null ? RebuildPrice(comp, proto) : null;
            int cost;
            if (comp == null)
                cost = 0;
            else if (rebuild is { } full && _destructible.TryGetDestroyedAt(ent.Owner, out var destroyedAt) && destroyedAt > FixedPoint2.Zero)
                cost = Math.Max(1, (int) Math.Ceiling(full * Math.Min(1f, total.Float() / destroyedAt.Value.Float())));
            else
                cost = Math.Max(1, Math.Min((int) Math.Ceiling(total.Float() * comp.DamagePrice), rebuild ?? int.MaxValue));

            items.Add(new WGDrydockWorkItem
            {
                Kind = WGDrydockWorkKind.Heal,
                Target = ent,
                Cost = cost,
                Prototype = proto?.ID,
                Share = rebuild is > 0 ? Math.Min(1f, cost / (float) rebuild.Value) : 1f,
                LocalPos = xform.LocalPosition,
            });
        }
    }

    /// <summary>
    /// Price of rebuilding one entity, plus labor: its estimated price times the factor, or the value of its construction
    /// materials times theirs, whichever is more.
    /// </summary>
    public int RebuildPrice(WGDrydockConsoleComponent comp, EntityPrototype proto)
    {
        if (!_estimates.TryGetValue(proto.ID, out var prices))
        {
            prices = (_pricing.GetEstimatedPrice(proto), MaterialValue(ConstructionMaterials(proto)));
            _estimates[proto.ID] = prices;
        }

        var price = Math.Max(prices.Estimate * comp.EntityPriceFactor, prices.Materials * comp.MaterialPriceFactor);
        return (int) Math.Min(price + comp.EntityLabor, int.MaxValue);
    }

    /// <summary>Applies one work item to the ship. Re-checks the item first, so repeats are harmless.</summary>
    public void ApplyItem(EntityUid ship, WGDrydockWorkItem item)
    {
        switch (item.Kind)
        {
            case WGDrydockWorkKind.Tile:
                ApplyTile(ship, item);
                break;
            case WGDrydockWorkKind.Entity:
                ApplyEntity(ship, item);
                break;
            case WGDrydockWorkKind.Heal:
                ApplyHeal(item);
                break;
            case WGDrydockWorkKind.Upgrade:
                ApplyUpgrade(item);
                break;
        }
    }

    private void ApplyTile(EntityUid ship, WGDrydockWorkItem item)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid) ||
            _map.GetTileRef(ship, grid, item.TileIndex).Tile.TypeId == item.TileType)
            return;

        _map.SetTile(ship, grid, item.TileIndex, new Tile(item.TileType));
    }

    private void ApplyEntity(EntityUid ship, WGDrydockWorkItem item)
    {
        if (!TryComp<ShipRepairDataComponent>(ship, out var data) ||
            !data.Chunks.TryGetValue(item.ChunkPos, out var chunk) ||
            !chunk.Entities.TryGetValue(item.SpecId, out var spec) ||
            !IsMissing(ship, spec) ||
            IsSpotTaken(ship, data, chunk, spec))
            return;

        // Only a drifting or debris original can still be here; remove it before rebuilding.
        if (TryGetOriginal(spec, out var original))
            QueueDel(original);

        var entity = Spawn(data.EntityPalette[spec.ProtoIndex].Id, new EntityCoordinates(ship, spec.LocalPosition));
        _transform.SetLocalRotation(entity, spec.Rotation);
        // The snapshot only holds anchored entities, but some prototypes, such as the mobile drydock crane, spawn loose.
        if (!Transform(entity).Anchored)
            _transform.AnchorEntity(entity);
        spec.OriginalEntity = GetNetEntity(entity);
        Dirty(ship, data);
        // Rebuilds spawn stock; reapply the ship's owned refits, which the quote charged for. A refit that replaces the
        // entity, as hull plating does, repoints the spec itself.
        foreach (var (refit, owned) in _refits.Owned(ship))
        {
            _refits.Apply(refit, entity, owned.Tier);
        }
    }

    private void ApplyHeal(WGDrydockWorkItem item)
    {
        if (!TryComp<DamageableComponent>(item.Target, out var damageable) || TerminatingOrDeleted(item.Target))
            return;

        // A crash-faulted APC has no damage; the repair clears the fault the way a multitool would.
        RemComp<WGCrashApcFaultComponent>(item.Target);
        if (damageable.TotalDamage <= FixedPoint2.Zero)
            return;

        _damageable.SetAllDamage(item.Target, damageable, FixedPoint2.Zero);
    }

    private void ApplyUpgrade(WGDrydockWorkItem item)
    {
        if (item.Upgrade == null ||
            !_prototype.TryIndex<WGShipRefitPrototype>(item.Upgrade, out var proto) ||
            TerminatingOrDeleted(item.Target))
            return;

        // A refit leaves units already at the tier alone.
        _refits.Apply(proto, item.Target, item.Tier);
    }

    /// <summary>
    /// Pushes the console state to viewers, and each viewer their own prices, skipping unchanged states unless forced (a
    /// viewer just opened the window).
    /// </summary>
    public void UpdateUi(EntityUid console, WGDrydockConsoleComponent? comp = null, bool force = false)
    {
        if (!Resolve(console, ref comp))
            return;

        var job = CompOrNull<WGDrydockJobComponent>(console);

        // During a job, report its ship even if it drifted, and count the queue instead of scanning the grid.
        var ship = job?.Ship ?? GetBerthedShip(console, comp);
        if (ship is { } shown && TerminatingOrDeleted(shown))
            ship = null;

        var state = BuildState(console, ship, comp, job);
        state.BerthLightsOn = comp.BerthLightsUntil != null;
        if (comp.Locked && state.Notice == null)
            state.Notice = Loc.GetString("drydock-console-locked-notice");

        // The state is shared, but a commanded ship's price rules price each viewer apart.
        var quotes = _ui.GetActors(console, WGDrydockConsoleUiKey.Key)
            .Select(actor => (Actor: actor, Quote: QuoteFor(console, actor, state)))
            .ToList();
        var key = StateKey(state) + "|" + string.Join(",", quotes.Select(q => $"{q.Actor}:{QuoteKey(q.Quote)}"));
        if (!force && key == comp.LastPushed)
            return;

        comp.LastPushed = key;
        _ui.SetUiState(console, WGDrydockConsoleUiKey.Key, state);
        foreach (var (actor, quote) in quotes)
            _ui.ServerSendUiMessage(console, WGDrydockConsoleUiKey.Key, new WGDrydockQuoteMessage(quote), actor);
    }

    /// <summary>What one customer pays for the work a console state lists: each of its prices through the ship's price rules.</summary>
    private WGDrydockQuote QuoteFor(EntityUid console, EntityUid viewer, WGDrydockConsoleState state)
    {
        var quote = new WGDrydockQuote
        {
            RepairPrice = _shipServices.Quote(console, viewer, WGShipServiceKind.Drydock, state.RepairPrice),
            CleanDriftPrice = _shipServices.Quote(console, viewer, WGShipServiceKind.Drydock, state.CleanDriftPrice),
        };

        foreach (var upgrade in state.Upgrades)
            quote.RefitPrices[upgrade.Id] = _shipServices.Quote(console, viewer, WGShipServiceKind.Drydock, upgrade.NextPrice);

        return quote;
    }

    /// <summary>Flattens a quote into a string, so unchanged quotes compare equal.</summary>
    private static string QuoteKey(WGDrydockQuote quote)
    {
        return $"{quote.RepairPrice}/{quote.CleanDriftPrice}/{DictKey(quote.RefitPrices)}";
    }

    /// <summary>
    /// Re-sends open consoles' state once a second, since ships, crane power and material stock change off the console.
    /// </summary>
    private void RefreshOpenConsoles(TimeSpan curTime)
    {
        if (curTime < _nextConsoleRefresh)
            return;

        _nextConsoleRefresh = curTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<WGDrydockConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (_ui.IsUiOpen(uid, WGDrydockConsoleUiKey.Key))
                UpdateUi(uid, comp);
        }
    }

    /// <summary>Flattens a console state into a string, so unchanged states compare equal.</summary>
    private static string StateKey(WGDrydockConsoleState s)
    {
        var parts = new List<string>
        {
            s.ShipName ?? "-",
            $"{s.MissingTiles}/{s.MissingParts}/{s.DamagedParts}/{s.RepairPrice}",
            $"{s.Working}/{s.PowerRatio}/{s.CraneCount}/{s.BlockedCranes}/{s.Deploying}/{s.Progress}/{s.RemainingSeconds}",
            $"{s.Booking}/{s.BookedBerth}/{s.BookingSeconds}/{s.Notice}/{s.PaysWithMaterials}/{s.BerthLightsOn}",
            $"{s.CanBreak}/{s.Breakable}/{s.Breaking}/{s.BreakOnly}/{s.BreakCash}/{s.BreakWarrant}/{s.BreakRefusal}/{s.CleanDriftPrice}",
            DictKey(s.BreakYield),
            $"{s.Station}/{s.BerthConsole}/{s.BerthLabel}/{s.BerthArea}/{s.Approach}/{s.Ship}/{s.Marks.Count}",
            DictKey(s.RepairMaterials),
            DictKey(s.RepairParts),
            DictKey(s.StoredMaterials),
            string.Join(",", s.StoredParts.OrderBy(p => p.Name).Select(p => $"{p.Prototype}:{p.Name}={p.Count}")),
            string.Join(",", s.StoredChemicals.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")),
        };
        foreach (var u in s.Upgrades)
        {
            parts.Add($"{u.Id}:{u.Name}:{u.Units}:{u.Tier}:{u.MaxTier}:{u.NextTier}:{u.NextPrice}:{DictKey(u.NextMaterials)}:{DictKey(u.NextParts)}");
        }

        return string.Join("|", parts);
    }

    private static string DictKey(Dictionary<string, int> bill)
    {
        return string.Join(",", bill.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
    }

    /// <summary>
    /// Builds the console state for a ship in or booked to a berth: its cranes, quote, job progress and refits.
    /// </summary>
    private WGDrydockConsoleState BuildState(EntityUid console, EntityUid? ship, WGDrydockConsoleComponent comp, WGDrydockJobComponent? job)
    {
        var linked = LinkedCranes(console);
        var working = WorkingCranes(console, comp);
        var state = new WGDrydockConsoleState
        {
            PowerRatio = working.Count == 0 ? 1f : working.Min(CranePower),
            CraneCount = linked.Count,
            BlockedCranes = linked.Count(uid => Comp<WGDrydockCraneComponent>(uid).Blocked),
        };

        if (TryGetBerth(console, comp, out var berth))
        {
            state.Station = GetNetEntity(berth.Grid);
            state.BerthConsole = GetNetEntity(console);
            state.BerthLabel = berth.Label;
            state.BerthArea = berth.Area;
            state.Approach = berth.Approach;
        }

        // A chop shop's material terms are only its break yields: it sells no work and stocks nothing.
        var chop = CompOrNull<WGDrydockChopShopComponent>(console);
        state.BreakOnly = chop != null;
        var pay = chop == null ? CompOrNull<WGDrydockMaterialPaymentComponent>(console) : null;
        if (pay != null)
        {
            state.PaysWithMaterials = true;
            // Same stock the job's charge draws from: the cranes' storage and their linked silos.
            state.StoredMaterials = Availability(working);
            state.StoredParts = SiloContents(working);
            state.StoredChemicals = StoredChemicals(working);
        }

        if (ship is { } berthed)
        {
            state.ShipName = MetaData(berthed).EntityName;
            state.Ship = GetNetEntity(berthed);

            var items = job != null ? UnfinishedItems(job) : chop != null ? new List<WGDrydockWorkItem>() : BuildRepairItems(berthed, comp);
            CountWork(state, items);
            AddMarks(state, items);

            if (job == null && items.Count > 0)
            {
                if (pay != null)
                {
                    AssignMaterials(pay, items, working);
                    state.RepairMaterials = SumBills(items);
                    state.RepairParts = SumParts(items);
                }
                else
                {
                    state.RepairPrice = WorkPrice(console, berthed, comp, null, items);
                }
            }

            // Credit consoles also clean Drift soaked into a hull by the Coda.
            if (job == null && chop == null && pay == null)
                state.CleanDriftPrice = _lattice.CleanDriftQuote(berthed);
        }

        if (CanBreak(console, out var breaker))
        {
            state.CanBreak = true;
            var refusal = job == null && ship is { } inBerth ? BreakRefusal(console, comp, inBerth) : null;
            state.BreakRefusal = refusal;
            if (job == null && ship is { } target && refusal == null)
            {
                state.Breakable = true;
                if (chop != null)
                {
                    state.BreakCash = ChopPayout(chop, target);
                    state.BreakWarrant = IsLawfulProperty(chop, target, null);
                }
                else
                {
                    state.BreakYield = BreakPreview(target, breaker);
                }
            }
        }

        if (job != null)
        {
            state.Working = true;
            state.Breaking = job.Break;
            state.Deploying = _timing.CurTime < job.WorkStarts;
            state.Progress = job.Total == 0 ? 0f : (float) job.Done / job.Total;
            state.RemainingSeconds = (float) Math.Max(0d, (job.EstimatedEnd - _timing.CurTime).TotalSeconds);
        }

        foreach (var id in chop == null ? comp.Refits : new())
        {
            if (_prototype.TryIndex(id, out var proto))
                state.Upgrades.Add(BuildRefitEntry(console, proto, ship, comp, pay, working, scan: job == null));
        }

        return state;
    }

    /// <summary>Tallies work items into the state's missing tile, missing part and damaged part counts.</summary>
    private static void CountWork(WGDrydockConsoleState state, List<WGDrydockWorkItem> items)
    {
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case WGDrydockWorkKind.Tile:
                case WGDrydockWorkKind.Break:
                    state.MissingTiles++;
                    break;
                case WGDrydockWorkKind.Entity:
                    state.MissingParts++;
                    break;
                case WGDrydockWorkKind.Heal:
                    state.DamagedParts++;
                    break;
            }
        }
    }

    /// <summary>Lists the work items' positions for the berth plot, up to its limit.</summary>
    private static void AddMarks(WGDrydockConsoleState state, List<WGDrydockWorkItem> items)
    {
        foreach (var item in items)
        {
            if (state.Marks.Count >= WGDrydockConsoleState.MaxMarks)
                break;

            var kind = item.Kind switch
            {
                WGDrydockWorkKind.Tile or WGDrydockWorkKind.Break => WGDrydockMarkKind.Tile,
                WGDrydockWorkKind.Entity => WGDrydockMarkKind.Part,
                WGDrydockWorkKind.Heal => WGDrydockMarkKind.Damage,
                _ => WGDrydockMarkKind.Refit,
            };
            state.Marks.Add(new WGDrydockPlotMark(item.LocalPos, kind));
        }
    }

    private void OnUiOpened(EntityUid uid, WGDrydockConsoleComponent comp, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, comp, force: true);
    }

    /// <summary>A window asks once it is open, in case the prices sent as it opened came before it.</summary>
    private void OnQuoteRequest(EntityUid uid, WGDrydockConsoleComponent comp, WGDrydockQuoteRequestMessage args)
    {
        UpdateUi(uid, comp, force: true);
    }

    private void OnRepair(EntityUid uid, WGDrydockConsoleComponent comp, WGDrydockRepairMessage args)
    {
        if (WalkInLocked(uid, comp, args.Actor))
            return;

        if (TryGetWorkableShip(uid, comp, args.Actor, out var berthed))
            TryStartRepair(uid, comp, berthed, args.Actor);
    }

    private void OnUpgrade(EntityUid uid, WGDrydockConsoleComponent comp, WGDrydockUpgradeMessage args)
    {
        if (WalkInLocked(uid, comp, args.Actor))
            return;

        // BuildRefitWork refuses a refit this console doesn't sell.
        if (TryGetWorkableShip(uid, comp, args.Actor, out var berthed))
            TryStartWork(uid, comp, berthed, args.Actor, args.Upgrade);
    }

    /// <summary>Cleans the Drift soaked into the berthed hull, at once, for the quoted price.</summary>
    private void OnCleanDrift(EntityUid uid, WGDrydockConsoleComponent comp, WGDrydockCleanDriftMessage args)
    {
        if (WalkInLocked(uid, comp, args.Actor))
            return;

        if (HasComp<WGDrydockChopShopComponent>(uid) || HasComp<WGDrydockMaterialPaymentComponent>(uid))
            return;

        // Cleaning needs no cranes and leaves any booking for the berthed ship in place.
        if (GetBerthedShip(uid, comp) is not { } berthed)
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-no-ship"));
            return;
        }

        if (HasComp<WGDrydockJobComponent>(uid))
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-busy"));
            return;
        }

        if (TryComp<WGDrydockBookingComponent>(uid, out var booking) && booking.Ship != berthed)
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-reserved", ("berth", BerthLabel(uid, comp)), ("ship", Name(booking.Ship))));
            return;
        }

        if (IsShipyardGrid(uid, comp, berthed))
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-own-grid"));
            return;
        }

        var price = _lattice.CleanDriftQuote(berthed);
        if (price <= 0)
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-no-drift"));
            return;
        }

        var basePrice = price;
        price = _shipServices.Quote(uid, args.Actor, WGShipServiceKind.Drydock, basePrice, out var rule);
        if (price > 0 && !TryCharge(uid, comp, args.Actor, price))
            return;

        _lattice.CleanDrift(berthed);
        _shipServices.Record(uid, args.Actor, WGShipServiceKind.Drydock, Loc.GetString("wg-ship-service-drift"), basePrice, price, rule);
        _popup.PopupCursor(Loc.GetString("drydock-console-drift-cleaned", ("price", price.ToString("N0"))), args.Actor);
        UpdateUi(uid, comp, true);
    }

    /// <summary>Starts a repair on the ship in this berth, paid by the actor.</summary>
    public bool TryStartRepair(EntityUid console, WGDrydockConsoleComponent comp, EntityUid ship, EntityUid actor)
    {
        return TryStartWork(console, comp, ship, actor, null);
    }

    /// <summary>Validates and charges a repair (null upgrade) or refit, then starts its job on the ship.</summary>
    private bool TryStartWork(EntityUid console, WGDrydockConsoleComponent comp, EntityUid ship, EntityUid actor, string? upgrade, bool booked = false)
    {
        if (HasComp<WGDrydockChopShopComponent>(console))
            return Deny(console, comp, actor, Loc.GetString("drydock-chop-break-only"));

        // A booking made before its payer was barred doesn't start; walk-in work was refused at the console.
        if (booked && _shipServices.Refuses(console, actor, out var refusal))
            return Deny(console, comp, actor, refusal);

        if (BuildWork(ship, comp, upgrade, out var items) is { } denial)
            return Deny(console, comp, actor, denial);

        if (!CranesReady(console, comp, actor, out var cranes))
            return false;

        // Booked jobs are always paid in credits; walk-in material consoles pay from their cranes.
        var pay = booked ? null : CompOrNull<WGDrydockMaterialPaymentComponent>(console);
        if (pay != null)
        {
            AssignMaterials(pay, items, cranes);

            // A short repair does what the stock covers; a refit is all or nothing.
            if (upgrade == null && Affordable(items, Availability(cranes)) is { Count: > 0 } doable)
                items = doable;

            if (!TryPayMaterials(console, comp, actor, items))
                return false;

            StartJob(console, comp, ship, null, items, 0, paidInMaterials: true);
            _popup.PopupCursor(Loc.GetString("drydock-console-started-materials", ("count", items.Count)), actor);
            return true;
        }

        // A repair voucher pays for a whole repair in place of credits.
        if (upgrade == null && TryStartVoucherRepair(console, comp, ship, actor, items))
            return true;

        // A commanded ship's price rules set what this customer pays; a free job charges nothing.
        var basePrice = WorkPrice(console, ship, comp, upgrade, items);
        var price = _shipServices.Quote(console, actor, WGShipServiceKind.Drydock, basePrice, out var rule);
        if (price > 0 && !TryCharge(console, comp, actor, price))
            return false;

        StartJob(console, comp, ship, actor, items, price, refundScale: basePrice > 0 ? price / (float) basePrice : 1f);
        _shipServices.Record(console, actor, WGShipServiceKind.Drydock, upgrade ?? Loc.GetString("wg-ship-service-repair"), basePrice, price, rule);
        return true;
    }

    /// <summary>Checks the console's working cranes can start a job: unblocked, at half power and free. Denies if not.</summary>
    private bool CranesReady(EntityUid console, WGDrydockConsoleComponent comp, EntityUid actor, out List<EntityUid> cranes)
    {
        RefreshCranes(console);
        cranes = WorkingCranes(console, comp);
        if (cranes.Count == 0 && LinkedCranes(console).Count > 0)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-cranes-blocked"));

        // Every working crane needs half its power to start.
        if (cranes.Any(c => CranePower(c) < 0.5f))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-power"));

        if (cranes.Any(c => CraneBusyElsewhere(console, c)))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-crane-busy"));

        return true;
    }

    /// <summary>
    /// Finds the berthed ship and checks the drydock can start on it: idle, unreserved for others, and held still.
    /// Clears the console's booking on success.
    /// </summary>
    private bool TryGetWorkableShip(EntityUid console, WGDrydockConsoleComponent comp, EntityUid actor, out EntityUid ship)
    {
        ship = EntityUid.Invalid;
        if (GetBerthedShip(console, comp) is not { } berthed)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-ship"));

        if (HasComp<WGDrydockJobComponent>(console))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-busy"));

        if (TryComp<WGDrydockBookingComponent>(console, out var booking) && booking.Ship != berthed)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-reserved", ("berth", BerthLabel(console, comp)), ("ship", Name(booking.Ship))));

        if (!IsHeldStill(console, comp, berthed))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-ship-moving"));

        if (IsShipyardGrid(console, comp, berthed))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-own-grid"));

        if (booking != null)
            RemoveBooking(console, booking);

        ship = berthed;
        return true;
    }

    /// <summary>Whether a ship moves with the drydock's station no faster than the console's start speed.</summary>
    private bool IsHeldStill(EntityUid console, WGDrydockConsoleComponent comp, EntityUid ship)
    {
        var stationVelocity = Transform(console).GridUid is { } station ? _physics.GetMapLinearVelocity(station) : Vector2.Zero;
        return (_physics.GetMapLinearVelocity(ship) - stationVelocity).Length() <= comp.MaxStartSpeed;
    }

    /// <summary>Withdraws the price from the actor's bank. Shows the reason and plays the deny sound on failure.</summary>
    private bool TryCharge(EntityUid console, WGDrydockConsoleComponent comp, EntityUid actor, int price)
    {
        if (!TryComp<BankAccountComponent>(actor, out var bank))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-bank-account"));

        if (!HasCharacter(actor))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-no-character"));

        if (bank.Balance < price)
            return Deny(console, comp, actor, Loc.GetString("drydock-console-insufficient-funds", ("price", price.ToString("N0"))));

        // The entity overload also updates the actor's cached balance, which the bank UI reads.
        if (!_bank.TryBankWithdraw(actor, price))
            return Deny(console, comp, actor, Loc.GetString("drydock-console-transaction-failed"));

        return true;
    }

    /// <summary>Whether the actor is a connected player with a selected humanoid character.</summary>
    private bool HasCharacter(EntityUid actor)
    {
        return TryComp<ActorComponent>(actor, out var actorComp) &&
               _prefs.TryGetCachedPreferences(actorComp.PlayerSession.UserId, out var prefs) &&
               prefs.SelectedCharacter is HumanoidCharacterProfile;
    }

    /// <summary>True when the ship is the console's grid or the grid of one of its berth cranes.</summary>
    private bool IsShipyardGrid(EntityUid console, WGDrydockConsoleComponent comp, EntityUid ship)
    {
        if (Transform(console).GridUid == ship)
            return true;

        return TryGetBerth(console, comp, out var berth) && berth.Cranes.Any(c => Transform(c).GridUid == ship);
    }

    /// <summary>Starts a paid job: queues its items, then deploys the cranes over the ship.</summary>
    public void StartJob(EntityUid console, WGDrydockConsoleComponent comp, EntityUid ship, EntityUid? payer, List<WGDrydockWorkItem> items, int price, bool paidInMaterials = false, string? voucher = null, float refundScale = 1f)
    {
        if (IsShipyardGrid(console, comp, ship))
        {
            Log.Warning($"Refused a drydock job on its own grid {ToPrettyString(ship)} from {ToPrettyString(console)}.");
            return;
        }

        TurnOffBerthLights(console, comp);
        var job = AddComp<WGDrydockJobComponent>(console);
        job.Ship = ship;
        job.Payer = payer;
        job.PaidInMaterials = paidInMaterials;
        job.Voucher = voucher;
        job.RefundScale = refundScale;
        job.Total = items.Count;
        job.Break = items[0].Kind == WGDrydockWorkKind.Break;
        job.ShipName = Name(ship);

        AddSlots(console, comp, job);
        CollectSparkTiles(ship, job);

        // Boom paths are cached before the cranes swing out, so their first targets use them.
        ScanJobBooms(job);

        var curTime = _timing.CurTime;
        var deploy = DeployCranes(comp, job);
        var maxDuration = items[0].Kind switch
        {
            WGDrydockWorkKind.Upgrade => comp.MaxRefitDuration,
            WGDrydockWorkKind.Break => comp.MaxBreakDuration,
            _ => comp.MaxDuration,
        };
        job.WorkStarts = curTime + TimeSpan.FromSeconds(deploy);
        job.Deadline = curTime + TimeSpan.FromSeconds(maxDuration);
        QueueItems(comp, job, items, maxDuration - deploy);
        foreach (var item in items)
        {
            if (item.Kind == WGDrydockWorkKind.Upgrade && item.Upgrade is { } refit)
                job.Refits[refit] = Math.Max(job.Refits.GetValueOrDefault(refit), item.Tier);
        }
        job.NextSpark = job.WorkStarts;
        job.NextIndustry = curTime;
        job.NextBerthCheck = curTime;
        UpdateEstimate(job, curTime);

        if (payer is { } payerUid)
        {
            var message = items[0].Kind switch
            {
                WGDrydockWorkKind.Upgrade => Loc.GetString("drydock-console-refit-started", ("count", items.Count)),
                WGDrydockWorkKind.Break => Loc.GetString("drydock-console-break-started", ("ship", job.ShipName), ("count", items.Count)),
                _ => voucher != null
                    ? Loc.GetString("drydock-console-voucher-used", ("count", items.Count))
                    : Loc.GetString("drydock-console-started", ("count", items.Count), ("price", price.ToString("N0"))),
            };
            _popup.PopupCursor(message, payerUid);
        }

        _audio.PlayPvs(comp.ConfirmSound, console);
        UpdateUi(console, comp);
    }

    /// <summary>One slot per berth crane; a berth without cranes still gets one slot.</summary>
    private void AddSlots(EntityUid console, WGDrydockConsoleComponent comp, WGDrydockJobComponent job)
    {
        if (TryGetBerth(console, comp, out var berth))
        {
            foreach (var crane in berth.Cranes)
                job.Slots.Add(new WGDrydockJobSlot { Crane = crane });
        }

        if (job.Slots.Count == 0)
            job.Slots.Add(new WGDrydockJobSlot());
    }

    /// <summary>Sets each item's crane work time, scales the work to fit the budget in seconds, and queues it.</summary>
    private static void QueueItems(WGDrydockConsoleComponent comp, WGDrydockJobComponent job, List<WGDrydockWorkItem> items, float budget)
    {
        var workTotal = 0f;
        foreach (var item in items)
        {
            item.WorkTime = item.Kind switch
            {
                WGDrydockWorkKind.Tile => comp.TileWorkTime,
                WGDrydockWorkKind.Entity => comp.EntityWorkTime,
                WGDrydockWorkKind.Heal => comp.HealWorkTime,
                WGDrydockWorkKind.Break => item.WorkTime,
                _ => comp.UpgradeWorkTime,
            };
            workTotal += item.WorkTime;
        }

        var span = workTotal / job.Slots.Count;
        var scale = span > budget ? Math.Max(0f, budget) / span : 1f;
        foreach (var item in items)
        {
            item.WorkTime *= scale;
            job.Pending.Add(item);
        }
    }

    /// <summary>Records the centers of the ship's tiles for ambient sparks, up to MaxSparkTiles.</summary>
    private void CollectSparkTiles(EntityUid ship, WGDrydockJobComponent job)
    {
        if (!TryComp<MapGridComponent>(ship, out var grid))
            return;

        var tiles = _map.GetAllTiles(ship, grid);
        while (job.SparkTiles.Count < MaxSparkTiles && tiles.MoveNext(out var tile))
        {
            var indices = tile!.Value.GridIndices;
            job.SparkTiles.Add(new Vector2(indices.X + 0.5f, indices.Y + 0.5f));
        }
    }

    /// <summary>Shows a refusal to the actor, plays the deny sound and returns false.</summary>
    private bool Deny(EntityUid console, WGDrydockConsoleComponent comp, EntityUid actor, string message)
    {
        // At the cursor, so a caller working the console from their helm sees it too.
        _popup.PopupCursor(message, actor);
        _audio.PlayPvs(comp.DenySound, console);
        return false;
    }

    /// <summary>Whether a snapshot entity is gone, or its original drifts in space or sits on debris of the ship.</summary>
    private bool IsMissing(EntityUid ship, ShipRepairEntitySpecifier spec)
    {
        if (!TryGetOriginal(spec, out var original))
            return true;

        // A part whose floor was blown out is deparented into space; it is lost, as the repair tool treats it.
        var grid = Transform(original).GridUid;
        return grid == null || grid != ship && _hull.IsDebrisOf(grid, ship);
    }

    /// <summary>Whether something anchored since holds a snapshot entity's spot, such as a gun swapped onto its hardpoint.</summary>
    private bool IsSpotTaken(EntityUid ship, ShipRepairDataComponent data, ShipRepairChunk chunk, ShipRepairEntitySpecifier spec)
    {
        return TryComp<MapGridComponent>(ship, out var grid) &&
            _hull.IsSpotTaken((ship, grid), data.EntityPalette[spec.ProtoIndex], _hull.TileOf(ship, spec.LocalPosition), data, chunk);
    }

    /// <summary>Resolves a spec's recorded original entity, or false if it no longer exists.</summary>
    private bool TryGetOriginal(ShipRepairEntitySpecifier spec, out EntityUid original)
    {
        original = EntityUid.Invalid;
        if (spec.OriginalEntity is not { } net ||
            !TryGetEntity(net, out var resolved) ||
            resolved is not { } uid ||
            TerminatingOrDeleted(uid))
            return false;

        original = uid;
        return true;
    }
}
