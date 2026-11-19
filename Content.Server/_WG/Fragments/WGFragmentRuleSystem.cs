using System.Linq;
using System.Numerics;
using Content.Server._Mono.Store;
using Content.Server._WG.Operations;
using Content.Server._WG.Remade.Conversion;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared._Mono.Company;
using Content.Shared.Destructible;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.Fragments;

/// <summary>
/// Places a Monolith fragment about as far from each faction base as the other, swaps its mobs for Remade, sends waves
/// from its heart at whoever comes near, pays every faction when the heart breaks and removes the fragment at the end.
/// </summary>
public sealed partial class WGFragmentRuleSystem : StationEventSystem<WGFragmentRuleComponent>
{
    [Dependency] private CurrencyInjectionSystem _currency = default!;
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private WGSiteGridSystem _siteGrid = default!;

    /// <summary>Points sampled in the spawn circle on each boarded grid per wave.</summary>
    private const int NearbySamples = 64;

    /// <summary>A wave's size: the base, one more per <paramref name="perExtra"/> players, and room left under the cap.</summary>
    public static int WaveSize(int players, int alive, int baseSize, int perExtra, int maxAlive)
    {
        if (players <= 0)
            return 0;
        var size = baseSize + (perExtra > 0 ? players / perExtra : 0);
        return Math.Clamp(size, 0, Math.Max(0, maxAlive - alive));
    }

    /// <summary>A faction's payout, raised by the joint bonus when both factions were aboard.</summary>
    public static int Payout(int reward, bool joint, float bonus)
    {
        return joint ? (int) MathF.Round(reward * (1f + bonus)) : reward;
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGFragmentHeartComponent, DestructionEventArgs>(OnHeartBroken);
        InitializeAnnouncements();
    }

    /// <summary>
    /// Records who is aboard as the heart breaks, freezes the list and pays out at once. Only a heart destroyed pays;
    /// one deleted any other way, such as with a piece of the fragment cleaned up, does not.
    /// </summary>
    private void OnHeartBroken(Entity<WGFragmentHeartComponent> ent, ref DestructionEventArgs args)
    {
        var rules = QueryActiveRules();
        while (rules.MoveNext(out _, out var comp, out _))
        {
            if (comp.HeartUid != ent.Owner || comp.HeartBroken || comp.Grid is not { } grid)
                continue;

            var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
            while (players.MoveNext(out var player, out _, out var state, out var xform))
            {
                if (xform.GridUid == grid && !_mobState.IsDead(player, state))
                    RecordVisit(comp, player);
            }
            comp.HeartBroken = true;
            Clear(comp);
        }
    }

    private void RecordVisit(WGFragmentRuleComponent comp, EntityUid player)
    {
        if (TryComp<CompanyComponent>(player, out var company) && comp.Rewards.ContainsKey(company.CompanyName))
            comp.Present.Add(company.CompanyName);
    }

    protected override void Started(EntityUid uid, WGFragmentRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);

        var mapId = GameTicker.DefaultMap;
        if (!MapSystem.MapExists(mapId) || comp.Grids.Count == 0)
            return;

        if (PickSite(comp, mapId) is not { } site)
        {
            Log.Warning("Fragment found no point clear of every faction base; not placed this time.");
            return;
        }

        var path = RobustRandom.Pick(comp.Grids);
        if (!_loader.TryLoadGrid(mapId, path, out var grid, offset: site))
        {
            Log.Error($"Fragment failed to load {path}");
            return;
        }

        comp.Grid = grid.Value;
        comp.Site = new MapCoordinates(site, mapId);
        _metaData.SetEntityName(grid.Value, Loc.GetString("wg-fragment-grid-name"));

        foreach (var child in _siteGrid.Children(grid.Value))
        {
            if (MetaData(child).EntityPrototype is not { } proto
                || !comp.Replace.TryGetValue(proto.ID, out var pool)
                || pool.Count == 0)
                continue;

            var coords = Transform(child).Coordinates;
            Del(child);
            comp.Remade.Add(SpawnRemade(RobustRandom.Pick(pool), coords));
        }

        var tiles = _siteGrid.OpenTiles(grid.Value);
        if (tiles.Count == 0)
            tiles.Add(new EntityCoordinates(grid.Value, Vector2.Zero));

        // The heart sits on the open tile nearest the fragment's middle.
        var centre = tiles.Aggregate(Vector2.Zero, (sum, t) => sum + t.Position) / tiles.Count;
        var heartAt = tiles.MinBy(t => (t.Position - centre).LengthSquared());
        comp.HeartUid = Spawn(comp.Heart, heartAt);

        for (var i = 0; i < comp.DecorCount && comp.Decor.Count > 0; i++)
            Spawn(RobustRandom.Pick(comp.Decor), RobustRandom.Pick(tiles));

        for (var i = 0; i < comp.Garrison && comp.Common.Count > 0; i++)
            comp.Remade.Add(SpawnRemade(RobustRandom.Pick(comp.Common), RobustRandom.Pick(tiles)));
        for (var i = 0; i < comp.GarrisonElites && comp.Elites.Count > 0; i++)
            comp.Remade.Add(SpawnRemade(RobustRandom.Pick(comp.Elites), RobustRandom.Pick(tiles)));

        comp.NextWave = Timing.CurTime + comp.WaveInterval;
        Announce(comp, comp.Arrived, comp.Site);
    }

    protected override void ActiveTick(EntityUid uid, WGFragmentRuleComponent comp, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, comp, gameRule, frameTime);

        // The station event times the end warning; the fragment sends it in its own voice.
        if (!comp.WarningSent && comp.Grid != null && TryComp<StationEventComponent>(uid, out var stationEvent)
            && stationEvent.WarningAnnounced)
        {
            comp.WarningSent = true;
            Announce(comp, comp.Warning, comp.Site, calm: comp.HeartBroken);
        }

        if (comp.Cleared || comp.Grid is not { } grid || !Exists(grid) || Timing.CurTime < comp.NextCheck)
            return;
        comp.NextCheck = Timing.CurTime + TimeSpan.FromSeconds(1);

        comp.Remade.RemoveWhere(r => !Exists(r) || _mobState.IsDead(r));

        var heartPos = comp.HeartUid is { } living && !TerminatingOrDeleted(living)
            ? _transform.GetMapCoordinates(living)
            : MapCoordinates.Nullspace;
        var near = new List<Vector2>();
        var boarded = new HashSet<EntityUid>();
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var state, out var xform))
        {
            if (_mobState.IsDead(player, state))
                continue;

            if (xform.GridUid == grid && !comp.HeartBroken)
                RecordVisit(comp, player);

            if (xform.MapID != heartPos.MapId)
                continue;
            var pos = _transform.GetWorldPosition(xform);
            if ((pos - heartPos.Position).Length() > comp.WakeRadius)
                continue;

            near.Add(pos);
            if (xform.GridUid is { } aboard && aboard != grid)
                boarded.Add(aboard);
        }

        // A heart gone without breaking pays nothing; the fragment just stops sending waves.
        if (heartPos == MapCoordinates.Nullspace)
            return;

        if (Timing.CurTime < comp.NextWave)
            return;
        comp.NextWave = Timing.CurTime + comp.WaveInterval;

        // Remade on the fragment, aboard a ship alongside or loose near the heart hold a place under the cap; those
        // carried off on a ship that left don't.
        var alive = comp.Remade.Count(r =>
        {
            var xform = Transform(r);
            if (xform.GridUid is { } on && (on == grid || boarded.Contains(on)))
                return true;
            return xform.MapID == heartPos.MapId
                   && (_transform.GetWorldPosition(xform) - heartPos.Position).Length() <= comp.SpawnRadius;
        });
        var size = WaveSize(near.Count, alive, comp.WaveBase, comp.PlayersPerExtra, comp.MaxAlive);
        if (size == 0 || comp.Common.Count == 0)
            return;

        comp.Wave++;
        // The elite takes one of the wave's slots, so the wave never passes the cap.
        var elite = comp.Elites.Count > 0 && comp.EliteEvery > 0 && comp.Wave % comp.EliteEvery == 0;
        if (elite)
            size--;
        var own = OutOfSight(_siteGrid.OpenTiles((grid, Comp<MapGridComponent>(grid))), near, true);
        var nearby = OutOfSight(NearbyTiles(comp, boarded, heartPos), near, false);
        if (own.Count == 0 && nearby.Count == 0)
            return;

        for (var i = 0; i < size; i++)
            comp.Remade.Add(SpawnRemade(RobustRandom.Pick(comp.Common), WaveSpot(comp, own, nearby)));

        if (elite)
            comp.Remade.Add(SpawnRemade(RobustRandom.Pick(comp.Elites), WaveSpot(comp, own, nearby)));
    }

    /// <summary>A spot for one Remade: a nearby grid by <see cref="WGFragmentRuleComponent.NearbyShare"/>, else the fragment.</summary>
    private EntityCoordinates WaveSpot(WGFragmentRuleComponent comp, List<EntityCoordinates> own, List<EntityCoordinates> nearby)
    {
        var useNearby = nearby.Count > 0 && (own.Count == 0 || RobustRandom.Prob(comp.NearbyShare));
        return RobustRandom.Pick(useNearby ? nearby : own);
    }

    /// <summary>
    /// Open tiles within the spawn radius of the heart on the grids players near it are aboard, such as a ship
    /// alongside; a grid nobody near the heart is on takes no Remade. Points in the circle are sampled, so a capital
    /// alongside costs no more than a shuttle.
    /// </summary>
    private List<EntityCoordinates> NearbyTiles(WGFragmentRuleComponent comp, HashSet<EntityUid> boarded, MapCoordinates heart)
    {
        var tiles = new List<EntityCoordinates>();
        if (comp.SpawnRadius <= 0f)
            return tiles;

        foreach (var other in boarded)
        {
            if (!TryComp<MapGridComponent>(other, out var gridComp))
                continue;

            var seen = new HashSet<Vector2i>();
            for (var i = 0; i < NearbySamples; i++)
            {
                var point = heart.Position + new Angle(RobustRandom.NextDouble() * Math.Tau).ToVec()
                            * comp.SpawnRadius * MathF.Sqrt(RobustRandom.NextFloat());
                var indices = MapSystem.WorldToTile(other, gridComp, point);
                if (!seen.Add(indices)
                    || !MapSystem.TryGetTileRef(other, gridComp, indices, out var tile)
                    || !_siteGrid.IsOpen(tile))
                    continue;
                tiles.Add(MapSystem.GridTileToLocal(other, gridComp, indices));
            }
        }

        return tiles;
    }

    /// <summary>
    /// The tiles at least 6 m from every player near the heart; with none, all of them if <paramref name="fallback"/>,
    /// else none, so a cramped ship sends its share back to the fragment.
    /// </summary>
    private List<EntityCoordinates> OutOfSight(List<EntityCoordinates> tiles, List<Vector2> near, bool fallback)
    {
        var hidden = tiles.Where(s =>
        {
            var world = _transform.ToMapCoordinates(s).Position;
            return near.All(p => (p - world).Length() >= 6f);
        }).ToList();
        return hidden.Count > 0 || !fallback ? hidden : tiles;
    }

    protected override void Ended(EntityUid uid, WGFragmentRuleComponent comp, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, comp, gameRule, args);

        if (comp.Grid != null)
            Announce(comp, comp.Cleared ? comp.Departed : comp.Lost, comp.Site);

        // Living Remade that boarded a ship leave with the fragment; those on it go with its grid, and corpses stay.
        foreach (var remade in comp.Remade)
        {
            if (Exists(remade) && !_mobState.IsDead(remade) && Transform(remade).GridUid != comp.Grid)
                QueueDel(remade);
        }
        comp.Remade.Clear();

        if (comp.Grid is { } grid && Exists(grid))
            _lifecycle.UnparentPlayersFromGrid(grid, true);
        comp.Grid = null;
    }

    /// <summary>Pays every faction for the broken heart, with the joint bonus if each had members aboard.</summary>
    private void Clear(WGFragmentRuleComponent comp)
    {
        comp.Cleared = true;
        var joint = comp.Rewards.Keys.All(comp.Present.Contains);

        foreach (var (id, reward) in comp.Rewards)
        {
            if (!PrototypeManager.TryIndex(id, out var company) || company.CompanyUplinkCurrency is not { } currency)
                continue;
            var amount = Payout(reward, joint, comp.JointBonus);
            _currency.InjectCurrency(id, new Dictionary<string, FixedPoint2> { [currency] = amount });
        }

        Announce(comp, comp.Broken, comp.Site, joint);
    }

    /// <summary>Where the fragment lands, from the faction uplinks anchored on the map; carried handheld uplinks share the tags but aren't bases.</summary>
    private Vector2? PickSite(WGFragmentRuleComponent comp, MapId mapId)
    {
        var bases = new List<Vector2>();
        var uplinks = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (uplinks.MoveNext(out _, out var tag, out var xform))
        {
            if (xform.MapID == mapId && xform.Anchored && comp.Bases.Any(b => _tags.HasTag(tag, b)))
                bases.Add(_transform.GetWorldPosition(xform));
        }

        return SiteBetween(bases, comp.BaseClearance, comp.MaxOffset, RobustRandom);
    }

    /// <summary>
    /// A point on the line halfway between the two furthest-apart bases, so each faction travels about as far: the
    /// clearance from both plus up to <paramref name="maxOffset"/> sideways. With fewer than two bases, a point 2-4 km
    /// from the sector's middle. Null when no candidate is clear of every base.
    /// </summary>
    public static Vector2? SiteBetween(IReadOnlyList<Vector2> bases, float clearance, float maxOffset, IRobustRandom random)
    {
        bool Valid(Vector2 point) => bases.All(p => (p - point).Length() >= clearance);

        if (bases.Count < 2)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var point = random.NextVector2(2000f, 4000f);
                if (Valid(point))
                    return point;
            }
            return null;
        }

        var (a, b) = (bases[0], bases[1]);
        for (var i = 0; i < bases.Count; i++)
        {
            for (var j = i + 1; j < bases.Count; j++)
            {
                if ((bases[j] - bases[i]).LengthSquared() > (b - a).LengthSquared())
                    (a, b) = (bases[i], bases[j]);
            }
        }

        var mid = (a + b) / 2f;
        var axis = b - a;
        var half = axis.Length() / 2f;
        var along = half > 0 ? axis / (half * 2f) : Vector2.UnitX;
        var across = new Vector2(-along.Y, along.X);

        // Far enough sideways that two close bases are each the clearance away; nudged at most a tenth towards either.
        var minAcross = MathF.Sqrt(MathF.Max(0f, clearance * clearance - half * half));
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var side = random.Prob(0.5f) ? 1f : -1f;
            var point = mid
                        + across * side * random.NextFloat(minAcross, minAcross + maxOffset)
                        + along * random.NextFloat(-0.1f, 0.1f) * half;
            if (Valid(point))
                return point;
        }

        var furthest = mid + across * (minAcross + maxOffset);
        return Valid(furthest) ? furthest : null;
    }

    /// <summary>A Remade answering to no Monolith: it never starves, and nothing it kills turns.</summary>
    private EntityUid SpawnRemade(EntProtoId proto, EntityCoordinates at)
    {
        var remade = Spawn(proto, at);
        EnsureComp<WGRemadeComponent>(remade);
        return remade;
    }
}
