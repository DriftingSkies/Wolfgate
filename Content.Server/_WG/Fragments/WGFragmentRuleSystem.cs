using System.Linq;
using System.Numerics;
using Robust.Shared.Utility;
using Content.Server._Mono.Store;
using Content.Server._WG.AdminVessels;
using Content.Server._NF.Bank;
using Content.Server._WG.Companies;
using Content.Server._WG.Operations;
using Content.Server._WG.Remade.Conversion;
using Content.Server.Atmos.Components;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Preferences.Managers;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared._Mono.Company;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Doors.Components;
using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking.Components;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Physics;
using Content.Shared.Preferences;
using Content.Shared.Store.Components;
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
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private WGCompanySystem _companies = default!;
    [Dependency] private CurrencyInjectionSystem _currency = default!;
    [Dependency] private ExplosionSystem _explosion = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
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
        SubscribeLocalEvent<WGFragmentHeartComponent, BeforeDamageChangedEvent>(OnHeartDamage);
        SubscribeLocalEvent<WGFragmentHullComponent, BeforeDamageChangedEvent>(OnHullDamage);
        SubscribeLocalEvent<WGFragmentHullComponent, GetExplosionResistanceEvent>(OnHullBlast);
        InitializeAnnouncements();
    }

    /// <summary>
    /// Makes the fragment's floors blast-proof and every wall, door and window on it indestructible, so it has to be
    /// boarded. Blasts then stop at the hull instead of reaching what's inside.
    /// </summary>
    public void SealHull(EntityUid grid)
    {
        EnsureComp<WGBlastproofFloorsComponent>(grid);
        if (!TryComp<MapGridComponent>(grid, out var mapGrid))
            return;

        foreach (var child in _siteGrid.Children(grid))
        {
            if (!HasComp<AirtightComponent>(child) && !HasComp<DoorComponent>(child))
                continue;

            EnsureComp<WGFragmentHullComponent>(child);
            var xform = Transform(child);
            if (xform.Anchored)
                _explosion.UpdateAirtightMap(grid, mapGrid, MapSystem.LocalToTile(grid, mapGrid, xform.Coordinates));
        }
    }

    private void OnHullDamage(Entity<WGFragmentHullComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Damage.AnyPositive())
            args.Cancelled = true;
    }

    private void OnHullBlast(Entity<WGFragmentHullComponent> ent, ref GetExplosionResistanceEvent args)
    {
        args.DamageCoefficient = 0;
    }

    /// <summary>
    /// Cancels harm to the heart unless it comes from someone aboard the fragment within reach: no ship guns, no bombs
    /// set off in space beside it, no ramming.
    /// </summary>
    private void OnHeartDamage(Entity<WGFragmentHeartComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive())
            return;

        if (!CanHurtHeart(ent, args.Origin))
            args.Cancelled = true;
    }

    /// <summary>Whether <paramref name="origin"/> stands on the heart's grid within its reach.</summary>
    public bool CanHurtHeart(Entity<WGFragmentHeartComponent> heart, EntityUid? origin)
    {
        if (origin is not { } attacker || TerminatingOrDeleted(attacker))
            return false;

        var heartXform = Transform(heart);
        var attackerXform = Transform(attacker);
        if (heartXform.GridUid is not { } grid || attackerXform.GridUid != grid)
            return false;

        var heartPos = _transform.GetWorldPosition(heartXform);
        var attackerPos = _transform.GetWorldPosition(attackerXform);
        return (heartPos - attackerPos).LengthSquared() <= heart.Comp.Reach * heart.Comp.Reach;
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
            while (players.MoveNext(out var player, out var actor, out var state, out var xform))
            {
                if (xform.GridUid == grid && !_mobState.IsDead(player, state))
                    RecordVisit(comp, player, actor);
            }
            comp.HeartBroken = true;
            Clear(comp);
        }
    }

    private void RecordVisit(WGFragmentRuleComponent comp, EntityUid player, ActorComponent actor)
    {
        RecordParticipant(comp, actor.PlayerSession);
        if (TryComp<CompanyComponent>(player, out var company) && comp.Rewards.ContainsKey(company.CompanyName))
            comp.Present.Add(company.CompanyName);
    }

    /// <summary>Notes the character slot a player takes part as, the first time they do.</summary>
    private void RecordParticipant(WGFragmentRuleComponent comp, ICommonSession session)
    {
        if (!comp.Participants.ContainsKey(session.UserId) && _companies.PlayedSlot(session) is { } slot)
            comp.Participants[session.UserId] = slot;
    }

    /// <summary>
    /// Picks the fragment's grid from every size, and if it belongs to a larger size takes that size's waves, heart,
    /// payouts and announcements over the defaults.
    /// </summary>
    private void ChooseSize(WGFragmentRuleComponent comp)
    {
        var pool = new List<(ResPath Path, WGFragmentSize? Size)>();
        pool.AddRange(comp.Grids.Select(g => (g, (WGFragmentSize?) null)));
        foreach (var size in comp.Sizes)
        {
            pool.AddRange(size.Grids.Select(g => (g, (WGFragmentSize?) size)));
        }

        if (pool.Count == 0)
            return;

        var (path, chosen) = RobustRandom.Pick(pool);
        comp.Chosen = path;
        if (chosen is not { } s)
            return;

        comp.GridName = s.GridName ?? comp.GridName;
        comp.Heart = s.Heart ?? comp.Heart;
        comp.Garrison = s.Garrison ?? comp.Garrison;
        comp.GarrisonElites = s.GarrisonElites ?? comp.GarrisonElites;
        comp.WaveBase = s.WaveBase ?? comp.WaveBase;
        comp.PlayersPerExtra = s.PlayersPerExtra ?? comp.PlayersPerExtra;
        comp.WaveInterval = s.WaveInterval ?? comp.WaveInterval;
        comp.EliteEvery = s.EliteEvery ?? comp.EliteEvery;
        comp.MaxAlive = s.MaxAlive ?? comp.MaxAlive;
        comp.DecorCount = s.DecorCount ?? comp.DecorCount;
        if (s.Scatter.Count > 0)
            comp.Scatter = s.Scatter;
        comp.ScatterCount = s.ScatterCount ?? comp.ScatterCount;
        comp.ParticipantReward = s.ParticipantReward ?? comp.ParticipantReward;
        foreach (var id in comp.Rewards.Keys.ToList())
        {
            comp.Rewards[id] = (int) MathF.Round(comp.Rewards[id] * s.RewardScale);
        }

        comp.Sighted = s.Sighted ?? comp.Sighted;
        comp.Arrived = s.Arrived ?? comp.Arrived;
        comp.Warning = s.Warning ?? comp.Warning;
        comp.Broken = s.Broken ?? comp.Broken;
        comp.Lost = s.Lost ?? comp.Lost;
        comp.Departed = s.Departed ?? comp.Departed;
    }

    protected override void Started(EntityUid uid, WGFragmentRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);

        var mapId = GameTicker.DefaultMap;
        if (!MapSystem.MapExists(mapId) || comp.Chosen == null && comp.Grids.Count == 0)
            return;

        if (PickSite(comp, mapId) is not { } site)
        {
            Log.Warning("Fragment found no point clear of every faction base; not placed this time.");
            return;
        }

        var path = comp.Chosen ?? RobustRandom.Pick(comp.Grids);
        if (!_loader.TryLoadGrid(mapId, path, out var grid, offset: site))
        {
            Log.Error($"Fragment failed to load {path}");
            return;
        }

        comp.Grid = grid.Value;
        comp.Site = new MapCoordinates(site, mapId);
        _metaData.SetEntityName(grid.Value, Loc.GetString(comp.GridName));

        SealHull(grid.Value);

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

        // The rest lands over the next few ticks rather than all in this one.
        for (var i = 0; i < comp.DecorCount && comp.Decor.Count > 0; i++)
            comp.Pending.Add((RobustRandom.Pick(comp.Decor), RobustRandom.Pick(tiles), false));
        for (var i = 0; i < comp.ScatterCount && comp.Scatter.Count > 0; i++)
            comp.Pending.Add((RobustRandom.Pick(comp.Scatter), RobustRandom.Pick(tiles), false));
        for (var i = 0; i < comp.Garrison && comp.Common.Count > 0; i++)
            comp.Pending.Add((RobustRandom.Pick(comp.Common), RobustRandom.Pick(tiles), true));
        for (var i = 0; i < comp.GarrisonElites && comp.Elites.Count > 0; i++)
            comp.Pending.Add((RobustRandom.Pick(comp.Elites), RobustRandom.Pick(tiles), true));

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

        SpawnPending(comp);
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
        while (players.MoveNext(out var player, out var actor, out var state, out var xform))
        {
            if (_mobState.IsDead(player, state))
                continue;

            if (xform.GridUid == grid && !comp.HeartBroken)
                RecordVisit(comp, player, actor);

            if (xform.MapID != heartPos.MapId)
                continue;
            var pos = _transform.GetWorldPosition(xform);
            if ((pos - heartPos.Position).Length() > comp.WakeRadius)
                continue;

            near.Add(pos);
            if (!comp.HeartBroken)
                RecordParticipant(comp, actor.PlayerSession);
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
        }) + comp.Pending.Count(p => p.Remade);
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
            comp.Pending.Add((RobustRandom.Pick(comp.Common), WaveSpot(comp, own, nearby), true));

        if (elite)
            comp.Pending.Add((RobustRandom.Pick(comp.Elites), WaveSpot(comp, own, nearby), true));
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
            _currency.InjectCurrencyOnce(id, new Dictionary<string, FixedPoint2> { [currency] = amount });
        }

        PayParticipants(comp);

        Announce(comp, comp.Broken, comp.Site, joint);
    }

    /// <summary>Deposits the participant reward, untaxed, to every player who took part and is still connected.</summary>
    private void PayParticipants(WGFragmentRuleComponent comp)
    {
        if (comp.ParticipantReward <= 0)
            return;

        foreach (var (user, slot) in comp.Participants)
        {
            if (!_players.TryGetSessionById(user, out var session) || !Deposit(session, slot, comp.ParticipantReward))
                continue;
            ChatManager.DispatchServerMessage(session,
                Loc.GetString("wg-fragment-participant-paid", ("amount", comp.ParticipantReward)));
        }
    }

    /// <summary>
    /// Deposits untaxed into the bank of the character in <paramref name="slot"/>, through the body's account when that
    /// is the selected character so its balance stays in step, as company payroll does.
    /// </summary>
    private bool Deposit(ICommonSession session, int slot, int amount)
    {
        if (!_prefs.TryGetCachedPreferences(session.UserId, out var prefs)
            || !prefs.Characters.TryGetValue(slot, out var character)
            || character is not HumanoidCharacterProfile profile)
            return false;

        if (prefs.SelectedCharacterIndex == slot && session.AttachedEntity is { } body && HasComp<BankAccountComponent>(body))
            return _bank.TryBankDeposit(body, amount, tax: false);

        return _bank.TryBankDeposit(session, prefs, profile, amount, out _);
    }

    /// <summary>Where the fragment lands, from the faction uplinks anchored on the map; carried handheld uplinks share the tags but aren't bases.</summary>
    private Vector2? PickSite(WGFragmentRuleComponent comp, MapId mapId)
    {
        // One list per faction tag, so a faction with several uplinks (a base and a supercapital) still pairs with the other.
        var factions = comp.Bases.Select(_ => new List<Vector2>()).ToList();
        var uplinks = EntityQueryEnumerator<StoreComponent, TagComponent, TransformComponent>();
        while (uplinks.MoveNext(out _, out _, out var tag, out var xform))
        {
            if (xform.MapID != mapId || !xform.Anchored)
                continue;

            for (var i = 0; i < comp.Bases.Count; i++)
            {
                if (_tags.HasTag(tag, comp.Bases[i]))
                    factions[i].Add(_transform.GetWorldPosition(xform));
            }
        }

        return SiteBetween(factions.ElementAtOrDefault(0) ?? new(), factions.ElementAtOrDefault(1) ?? new(),
            comp.BaseClearance, comp.MaxOffset, RobustRandom);
    }

    /// <summary>
    /// A point on the line halfway between the nearest uplink of one faction and the nearest of the other, so each
    /// travels about as far: the clearance from every uplink plus up to <paramref name="maxOffset"/> sideways. With a
    /// faction missing, a point 2-4 km from the sector's middle. Null when no candidate is clear of every uplink.
    /// </summary>
    public static Vector2? SiteBetween(IReadOnlyList<Vector2> first, IReadOnlyList<Vector2> second, float clearance,
        float maxOffset, IRobustRandom random)
    {
        var bases = first.Concat(second).ToList();
        bool Valid(Vector2 point) => bases.All(p => (p - point).Length() >= clearance);

        if (first.Count == 0 || second.Count == 0)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                var point = random.NextVector2(2000f, 4000f);
                if (Valid(point))
                    return point;
            }
            return null;
        }

        var (a, b) = (first[0], second[0]);
        foreach (var p in first)
        {
            foreach (var q in second)
            {
                if ((q - p).LengthSquared() < (b - a).LengthSquared())
                    (a, b) = (p, q);
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

    /// <summary>
    /// Spawns the next few of the landing's pending entities; drops them if the fragment is gone, and drops pending Remade
    /// once it is cleared.
    /// </summary>
    private void SpawnPending(WGFragmentRuleComponent comp)
    {
        if (comp.Pending.Count == 0)
            return;

        if (comp.Grid is not { } grid || TerminatingOrDeleted(grid))
        {
            comp.Pending.Clear();
            return;
        }

        // Once the heart breaks, Remade still queued never arrive; the landing's decor still does.
        if (comp.Cleared)
            comp.Pending.RemoveAll(p => p.Remade);

        var count = Math.Min(WGFragmentRuleComponent.SpawnsPerTick, comp.Pending.Count);
        for (var i = 0; i < count; i++)
        {
            var (proto, at, remade) = comp.Pending[i];
            if (!at.IsValid(EntityManager))
                continue;

            if (remade)
                comp.Remade.Add(SpawnRemade(proto, at));
            else
                Spawn(proto, at);
        }

        comp.Pending.RemoveRange(0, count);
    }

    /// <summary>A Remade answering to no Monolith: it never starves, and nothing it kills turns.</summary>
    private EntityUid SpawnRemade(EntProtoId proto, EntityCoordinates at)
    {
        var remade = Spawn(proto, at);
        EnsureComp<WGRemadeComponent>(remade);
        return remade;
    }
}
