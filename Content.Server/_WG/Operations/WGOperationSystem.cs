using System.Linq;
using System.Numerics;
using Content.Server._Mono.Store;
using Content.Server.StationEvents.Events;
using Content.Server._WG.Ordnance;
using Content.Server._WG.PlanetPois;
using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.ShipTiers;
using Content.Server.Chat.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Spawners.Components;
using Content.Server.GameTicking;
using Content.Server.Warps;
using Content.Shared._Mono.Company;
using Content.Shared._WG.Operations;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Roles.Jobs;
using Content.Server.Store.Systems;
using Content.Shared.Physics;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WG.Operations;

/// <summary>
/// Runs operations called in from faction uplinks: places the site away from both bases, warns the sector, spawns the
/// site with its payloads and guards, pays out a cleared PvE site and removes the site when its time is up.
/// </summary>
public sealed class WGOperationSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private CurrencyInjectionSystem _currency = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGShipTierSystem _shipTiers = default!;

    [Dependency] private WGPlanetPoiSystem _pois = default!;
    [Dependency] private WGSiteGridSystem _siteGrid = default!;

    /// <summary>When each operation can be called again.</summary>
    private readonly Dictionary<string, TimeSpan> _readyAt = new();

    /// <summary>Each site grid's bounds, measured once, for claiming planet ground to fit it.</summary>
    private readonly Dictionary<ResPath, Box2> _bounds = new();

    private TimeSpan _nextCheck;

    /// <summary>
    /// The operation the uplink purchase being handled started, if any. The store raises the call, then
    /// <see cref="StoreBuyFinishedEvent"/> with the store, so the second tells which uplink paid for the first.
    /// </summary>
    private EntityUid? _justCalled;

    /// <summary>How long after arrival a PvE site keeps picking up guards its map spawns late.</summary>
    private static readonly TimeSpan GuardScanWindow = TimeSpan.FromSeconds(30);

    /// <summary>How far from a spawner's spot, in metres, a mob may appear and still count as that spawner's guard.</summary>
    private const float GuardSpawnRadius = 1.5f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CompanyComponent, WGCallOperationEvent>(OnCall);
        SubscribeLocalEvent<StoreBuyFinishedEvent>(OnBuyFinished);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _readyAt.Clear());
        SubscribeLocalEvent<RoundStartedEvent>(_ => MeasurePlanetGrids());
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);
    }

    /// <summary>Marks a site's guard as down when it goes critical or dies, so a guard gibbed afterwards still counts.</summary>
    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState is not (MobState.Critical or MobState.Dead))
            return;

        var query = EntityQueryEnumerator<WGActiveOperationComponent>();
        while (query.MoveNext(out _, out var active))
        {
            if (active.Npcs.Contains(args.Target))
                active.Downed.Add(args.Target);
        }
    }

    /// <summary>
    /// Whether the buyer can call this operation now: a member of its faction in one of its jobs, with none of the
    /// faction's operations running and this one off cooldown.
    /// </summary>
    public bool CanCall(EntityUid buyer, ProtoId<WGOperationPrototype> id)
    {
        if (!_protos.TryIndex(id, out var op)
            || !TryComp<CompanyComponent>(buyer, out var company)
            || company.CompanyName != op.Company)
            return false;

        if (op.Jobs.Count > 0
            && (!_mind.TryGetMind(buyer, out var mind, out _)
                || !_jobs.MindTryGetJobId(mind, out var job)
                || job is not { } jobId
                || !op.Jobs.Contains(jobId)))
            return false;

        if (_readyAt.TryGetValue(id, out var ready) && _timing.CurTime < ready)
            return false;

        if (op.PlanetChance >= 1f && _pois.OperationGrounds(op.ExcludedWorlds).Count == 0)
            return false;

        var query = EntityQueryEnumerator<WGActiveOperationComponent>();
        while (query.MoveNext(out _, out var active))
        {
            if (_protos.TryIndex(active.Operation, out var running) && running.Company == op.Company)
                return false;
        }
        return true;
    }

    private void OnCall(EntityUid uid, CompanyComponent company, WGCallOperationEvent args)
    {
        _justCalled = null;
        // A call that can't go ahead is refunded to the uplink that took the stake, in OnBuyFinished.
        if (!_protos.TryIndex(args.Operation, out var op) || !CanCall(uid, args.Operation))
            return;

        _justCalled = Start(op);
    }

    /// <summary>Starts an operation without a buyer: picks its site and warns the sector. Null when no site was found.</summary>
    public EntityUid? Start(WGOperationPrototype op)
    {
        var manager = Spawn(null, MapCoordinates.Nullspace);
        var active = AddComp<WGActiveOperationComponent>(manager);
        active.Operation = op.ID;

        // A planetside roll that finds no world falls back to space, unless the operation only runs planetside.
        var extra = TimeSpan.Zero;
        if (_random.Prob(Math.Clamp(op.PlanetChance, 0f, 1f)) && TryClaimPlanetSite(op, active))
            extra = op.PlanetExtraTime;
        else if (op.PlanetChance >= 1f || !TryPickSite(op, out active.Target))
        {
            Log.Warning($"No site for operation {op.ID}");
            QueueDel(manager);
            return null;
        }

        var inbound = op.Inbound + extra;
        active.ArrivesAt = _timing.CurTime + inbound;
        active.EndsAt = _timing.CurTime + inbound + op.Duration + extra;

        Announce("wg-operation-inbound", op, active, ("minutes", (int) inbound.TotalMinutes));
        return manager;
    }

    /// <summary>
    /// Ties an operation just called to the uplink that paid for it, so its payouts go back there alone; a call that
    /// didn't start gets its whole price back at that uplink.
    /// </summary>
    private void OnBuyFinished(ref StoreBuyFinishedEvent args)
    {
        if (args.PurchasedItem.ProductEvent is not WGCallOperationEvent call)
            return;

        var called = _justCalled;
        _justCalled = null;
        if (called is { } manager && TryComp<WGActiveOperationComponent>(manager, out var active)
            && !TerminatingOrDeleted(manager) && active.Operation == call.Operation)
        {
            active.Store = args.StoreUid;
            return;
        }

        if (!_protos.TryIndex(call.Operation, out var op))
            return;
        Log.Warning($"Operation {op.ID} was paid for but didn't start; refunding {ToPrettyString(args.StoreUid)}.");
        _currency.InjectCurrencyOnce(op.Company, args.PurchasedItem.Cost.ToDictionary(c => c.Key.Id, c => c.Value), args.StoreUid);
    }

    /// <summary>Claims fresh ground on a random built world for a planetside site, sized for the grid it will load.</summary>
    private bool TryClaimPlanetSite(WGOperationPrototype op, WGActiveOperationComponent active)
    {
        var grounds = _pois.OperationGrounds(op.ExcludedWorlds);
        if (grounds.Count == 0)
            return false;

        var path = _random.Pick(op.PlanetGrids.Count > 0 ? op.PlanetGrids : op.Grids);
        if (!TryMeasure(path, out var bounds))
            return false;

        _random.Shuffle(grounds);
        foreach (var ground in grounds)
        {
            if (!_pois.TryReserveOperationSite(ground, bounds.Size.Length() / 2f, op.Name, out var spot, out var index))
                continue;
            active.Ground = ground;
            active.SiteIndex = index;
            active.Path = path;
            active.Target = new MapCoordinates(spot, Transform(ground).MapID);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Measures every grid a planetside site may load, once per server, at the start of a round rather than in the
    /// middle of an uplink purchase.
    /// </summary>
    private void MeasurePlanetGrids()
    {
        foreach (var op in _protos.EnumeratePrototypes<WGOperationPrototype>())
        {
            if (op.PlanetChance <= 0f)
                continue;
            foreach (var path in op.PlanetGrids.Count > 0 ? op.PlanetGrids : op.Grids)
                TryMeasure(path, out _);
        }
    }

    /// <summary>A grid file's bounds, loaded once onto a paused scratch map and remembered.</summary>
    private bool TryMeasure(ResPath path, out Box2 bounds)
    {
        if (_bounds.TryGetValue(path, out bounds))
            return true;

        var scratch = _map.CreateMap(out var mapId, runMapInit: false);
        try
        {
            if (!_loader.TryLoadGrid(mapId, path, out var grid))
                return false;
            bounds = grid.Value.Comp.LocalAABB;
            _bounds[path] = bounds;
            return true;
        }
        finally
        {
            Del(scratch);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;
        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(1);

        var query = EntityQueryEnumerator<WGActiveOperationComponent>();
        while (query.MoveNext(out var uid, out var active))
        {
            if (!_protos.TryIndex(active.Operation, out var op))
            {
                QueueDel(uid);
                continue;
            }

            if (active.ArrivesAt is { } arrives)
            {
                if (_timing.CurTime >= arrives)
                    Arrive((uid, active), op);
                continue;
            }

            // Spawners on the map can produce their mobs a moment after the grid loads.
            if (op.Reward > 0 && !active.Paid && active.Grid is { } scanned && _timing.CurTime < active.ArrivedAt + GuardScanWindow)
            {
                AddMapGuards(active, scanned, true);
                // Turrets aren't guards, but one a spawner makes late still fights for the site.
                foreach (var child in _siteGrid.Children(scanned))
                {
                    if (MetaData(child).CreationTick >= active.ArrivedTick
                        && AtGuardSpawn(active, Transform(child).LocalPosition))
                        JoinGuardFaction(child, op);
                }
            }

            // A guard deleted without ever going down (despawned, cleaned up) neither holds up the clear nor pays for it.
            active.Npcs.RemoveAll(npc => !Exists(npc) && !active.Downed.Contains(npc));

            // A cleared PvE site pays at once and stays until its time is up, for the loot; a clear seen after the deadline fails.
            if (_timing.CurTime < active.EndsAt && !active.Paid && IsCleared(active, op))
            {
                active.Paid = true;
                Pay(op, active, op.Stake + op.Reward);
                SpawnSpoils(active, op);
                Announce("wg-operation-cleared", op, active);
            }

            if (_timing.CurTime < active.EndsAt)
                continue;

            if (active.Npcs.Count > 0 && !active.Paid)
            {
                Refund(op, active, op.FailRefund);
                Announce("wg-operation-failed", op, active);
            }
            else if (op.Reward > 0 && !active.Paid)
            {
                // A PvE site that never had a guard to clear: the caller gets its whole stake back.
                Refund(op, active, 1f);
                Announce("wg-operation-ended", op, active);
            }
            else
            {
                Announce("wg-operation-ended", op, active);
            }

            if (active.Grid is { } grid && Exists(grid))
                _lifecycle.UnparentPlayersFromGrid(grid, true);
            if (active.Ground is { } ground)
                _pois.ReleaseOperationSite(ground, active.SiteIndex);

            _readyAt[op.ID] = _timing.CurTime + op.Cooldown;
            QueueDel(uid);
        }
    }

    /// <summary>Loads the site at its target, swaps its mapped entities, and spawns its payloads and guards.</summary>
    private void Arrive(Entity<WGActiveOperationComponent> ent, WGOperationPrototype op)
    {
        ent.Comp.ArrivesAt = null;
        ent.Comp.ArrivedTick = _timing.CurTick;

        var loaded = TryLoadSite(ent.Comp, op, out var grid, out var path);

        // A ship that landed on the claimed ground while the site was inbound blocks it; it sets down elsewhere instead.
        if (!loaded && ent.Comp.Ground is { } blocked)
        {
            _pois.ReleaseOperationSite(blocked, ent.Comp.SiteIndex);
            ent.Comp.Ground = null;
            ent.Comp.SiteIndex = -1;
            ent.Comp.Path = null;
            if (TryClaimPlanetSite(op, ent.Comp) || op.PlanetChance < 1f && TryPickSite(op, out ent.Comp.Target))
                loaded = TryLoadSite(ent.Comp, op, out grid, out path);
        }

        if (!loaded || grid == null)
        {
            Log.Error($"Operation {op.ID} failed to load {path}");
            Refund(op, ent.Comp, 1f);
            Announce("wg-operation-scrubbed", op, ent.Comp);
            if (ent.Comp.Ground is { } claimed)
                _pois.ReleaseOperationSite(claimed, ent.Comp.SiteIndex);
            QueueDel(ent);
            return;
        }

        ent.Comp.Grid = grid.Value;
        _metaData.SetEntityName(grid.Value, Loc.GetString(op.Name));
        EntityManager.AddComponents(grid.Value, op.AddComponents);

        foreach (var child in _siteGrid.Children(grid.Value))
        {
            if (MetaData(child).EntityPrototype is not { } proto
                || !op.Replace.TryGetValue(proto.ID, out var replacement))
                continue;

            var coords = Transform(child).Coordinates;
            Del(child);
            Spawn(replacement, coords);
        }

        ent.Comp.ArrivedAt = _timing.CurTime;

        // On a PvE site, NPCs the map spawns itself guard it too; spawners still waiting mark where later ones appear.
        if (op.Reward > 0)
        {
            foreach (var child in _siteGrid.Children(grid.Value))
            {
                if (HasComp<ConditionalSpawnerComponent>(child) || HasComp<EntityTableSpawnerComponent>(child)
                    || HasComp<TimedSpawnerComponent>(child))
                    ent.Comp.GuardSpawns.Add(Transform(child).LocalPosition);
            }
            AddMapGuards(ent.Comp, grid.Value.Owner, false);
        }

        var tiles = _siteGrid.OpenTiles(grid.Value);
        if (tiles.Count == 0)
            tiles.Add(new EntityCoordinates(grid.Value, Vector2.Zero));

        var anchor = _siteGrid.Children(grid.Value).FirstOrDefault(HasComp<WarpPointComponent>);
        foreach (var payload in op.Payloads)
        {
            for (var i = 0; i < payload.Count; i++)
            {
                var at = Exists(anchor) && !payload.Scatter ? Transform(anchor).Coordinates : _random.Pick(tiles);
                Spawn(payload.Proto, at);
            }
        }

        var extra = op.NpcPerMembers > 0 && op.Npcs.Count > 0
            ? _shipTiers.MembersOnline(op.Company) / op.NpcPerMembers
            : 0;
        for (var n = 0; n < op.Npcs.Count; n++)
        {
            var count = op.Npcs[n].Count + (n == 0 ? extra : 0);
            for (var i = 0; i < count; i++)
            {
                var npc = Spawn(op.Npcs[n].Proto, _random.Pick(tiles));
                // Remade on a site answer to no Monolith: they never starve and nothing they kill turns.
                if (_faction.IsMember(npc, "WGRemade"))
                    EnsureComp<WGRemadeComponent>(npc);
                ent.Comp.Npcs.Add(npc);
                ent.Comp.Brought.Add(npc);
            }
        }

        // Everything that came with a PvE site fights for it, turrets included, whatever faction its map gave it.
        if (op.Reward > 0)
        {
            foreach (var child in _siteGrid.Children(grid.Value))
            {
                if (MetaData(child).CreationTick >= ent.Comp.ArrivedTick)
                    JoinGuardFaction(child, op);
            }
        }

        var stay = ent.Comp.EndsAt - _timing.CurTime;
        Announce(op.Reward > 0 ? "wg-operation-arrived-pve" : "wg-operation-arrived", op, ent.Comp, ("minutes", (int) Math.Round(stay.TotalMinutes)));
    }

    /// <summary>Loads the site's grid at its target, on the world it claimed or in space.</summary>
    private bool TryLoadSite(WGActiveOperationComponent active, WGOperationPrototype op,
        out Entity<MapGridComponent>? grid, out ResPath path)
    {
        grid = null;
        path = active.Path ?? _random.Pick(op.Grids);
        return active.Ground is { } ground
            ? _bounds.TryGetValue(path, out var bounds)
              && _pois.TryLoadOperationSite(ground, path, active.Target.Position, bounds.Center, bounds.Size.Length() / 2f, out grid)
            : _loader.TryLoadGrid(active.Target.MapId, path, out grid, offset: active.Target.Position);
    }

    /// <summary>
    /// Whether a PvE site is cleared: every guard the operation brought is down, and at least
    /// <see cref="WGOperationPrototype.ClearShare"/> of those its map spawned, so a mob hiding somewhere or a harmless
    /// one nobody thinks to kill doesn't hold the site up.
    /// </summary>
    public static bool IsCleared(WGActiveOperationComponent active, WGOperationPrototype op, Func<EntityUid, bool> down)
    {
        if (active.Npcs.Count == 0)
            return false;

        var mapped = 0;
        var mappedDown = 0;
        foreach (var npc in active.Npcs)
        {
            var isDown = down(npc);
            if (active.Brought.Contains(npc))
            {
                if (!isDown)
                    return false;
                continue;
            }
            mapped++;
            if (isDown)
                mappedDown++;
        }

        return mapped == 0 || mappedDown >= (int) MathF.Ceiling(mapped * Math.Clamp(op.ClearShare, 0f, 1f));
    }

    private bool IsCleared(WGActiveOperationComponent active, WGOperationPrototype op)
    {
        return IsCleared(active, op, npc => active.Downed.Contains(npc) || Exists(npc) && _mobState.IsIncapacitated(npc));
    }

    /// <summary>
    /// Counts the NPCs on the site's grid that came with it among its guards; wildlife that wanders on, made before the
    /// site, doesn't count. After arrival, with <paramref name="atSpawners"/>, only mobs that appear where one of the
    /// site's spawners sat count, so a pet or mob a player lets loose on the site never holds up its payout.
    /// </summary>
    private void AddMapGuards(WGActiveOperationComponent active, EntityUid grid, bool atSpawners)
    {
        if (atSpawners && active.GuardSpawns.Count == 0)
            return;

        foreach (var npc in _siteGrid.Children(grid))
        {
            // Turrets are anchored, and aren't guards.
            if (!HasComp<HTNComponent>(npc)
                || !HasComp<MobStateComponent>(npc)
                || HasComp<ActorComponent>(npc)
                || Transform(npc).Anchored
                || MetaData(npc).CreationTick < active.ArrivedTick
                || active.Npcs.Contains(npc))
                continue;

            if (atSpawners && !AtGuardSpawn(active, Transform(npc).LocalPosition))
                continue;

            active.Npcs.Add(npc);
        }
    }

    /// <summary>Whether a point on the site lies where one of its spawners sat at arrival.</summary>
    private static bool AtGuardSpawn(WGActiveOperationComponent active, Vector2 at)
    {
        return active.GuardSpawns.Any(spawn => (spawn - at).LengthSquared() <= GuardSpawnRadius * GuardSpawnRadius);
    }

    /// <summary>Moves an NPC or turret of a site into the operation's guard faction, if it names one; players are left alone.</summary>
    private void JoinGuardFaction(EntityUid uid, WGOperationPrototype op)
    {
        if (op.GuardFaction is not { } faction
            || HasComp<ActorComponent>(uid)
            || !TryComp<NpcFactionMemberComponent>(uid, out var member)
            || member.Factions.Count == 1 && _faction.IsMember((uid, member), faction))
            return;

        _faction.ClearFactions((uid, member), false);
        _faction.AddFaction((uid, member), faction);
    }

    /// <summary>Drops a cleared site's spoils at its warp point, or on open tiles.</summary>
    private void SpawnSpoils(WGActiveOperationComponent active, WGOperationPrototype op)
    {
        if (op.ClearSpawns.Count == 0 || active.Grid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return;

        var tiles = _siteGrid.OpenTiles((grid, gridComp));
        if (tiles.Count == 0)
            tiles.Add(new EntityCoordinates(grid, Vector2.Zero));
        var anchor = _siteGrid.Children(grid).FirstOrDefault(HasComp<WarpPointComponent>);

        foreach (var spoil in op.ClearSpawns)
        {
            for (var i = 0; i < spoil.Count; i++)
            {
                var at = Exists(anchor) && !spoil.Scatter ? Transform(anchor).Coordinates : _random.Pick(tiles);
                Spawn(spoil.Proto, at);
            }
        }
    }



    /// <summary>A random point in the operation's distance band, clear of both faction bases.</summary>
    private bool TryPickSite(WGOperationPrototype op, out MapCoordinates target)
    {
        target = MapCoordinates.Nullspace;
        var mapId = _ticker.DefaultMap;
        if (!_map.MapExists(mapId))
            return false;

        var bases = new List<Vector2>();
        var query = EntityQueryEnumerator<WGOrdnanceStationComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == mapId)
                bases.Add(_transform.GetWorldPosition(uid));
        }

        for (var attempt = 0; attempt < 30; attempt++)
        {
            var point = _random.NextVector2(op.MinDistance, op.MaxDistance);
            if (bases.Any(b => (b - point).Length() < op.BaseClearance))
                continue;

            target = new MapCoordinates(point, mapId);
            return true;
        }
        return false;
    }

    /// <summary>Pays the caller once, at the uplink that bought the operation if it still stands.</summary>
    private void Pay(WGOperationPrototype op, WGActiveOperationComponent active, int amount)
    {
        if (amount <= 0
            || !_protos.TryIndex(op.Company, out var company)
            || company.CompanyUplinkCurrency is not { } currency)
            return;

        _currency.InjectCurrencyOnce(op.Company, new Dictionary<string, FixedPoint2> { [currency] = amount }, active.Store);
    }

    private void Refund(WGOperationPrototype op, WGActiveOperationComponent active, float share)
    {
        Pay(op, active, (int) MathF.Round(op.Stake * share));
    }

    /// <summary>
    /// An announcement naming the site and its rough position, in space or on a named world, to the sector or only the
    /// caller's faction.
    /// </summary>
    private void Announce(string key, WGOperationPrototype op, WGActiveOperationComponent active, params (string, object)[] extra)
    {
        var at = active.Target.Position;
        var precision = Math.Max(1, active.Ground != null ? op.PlanetPrecision : op.Precision);
        var x = (int) MathF.Round(at.X / precision) * precision;
        var y = (int) MathF.Round(at.Y / precision) * precision;
        var location = active.Ground is { } world
            ? Loc.GetString("wg-operation-location-planet", ("world", _pois.OperationWorldName(world)), ("x", x), ("y", y))
            : Loc.GetString("wg-operation-location-space", ("x", x), ("y", y));

        var faction = _protos.TryIndex(op.Company, out var company) ? company.Name : op.Company.Id;
        var args = new List<(string, object)>
        {
            ("faction", faction),
            ("site", Loc.GetString(op.Name)),
            ("location", location),
        };
        args.AddRange(extra);
        var message = Loc.GetString(key, args.ToArray());
        var sender = Loc.GetString("wg-operation-announcer");

        if (op.Broadcast == WGOperationBroadcast.Sector)
        {
            _chat.DispatchGlobalAnnouncement(message, sender, colorOverride: Color.Gold);
            return;
        }

        var filter = Filter.Empty().AddWhereAttachedEntity(e =>
            TryComp<CompanyComponent>(e, out var member) && member.CompanyName == op.Company);
        _chat.DispatchFilteredAnnouncement(filter, message, sender: sender, colorOverride: Color.Gold);
    }
}
