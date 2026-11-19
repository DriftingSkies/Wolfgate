using System.Linq;
using System.Numerics;
using Content.Server._Mono.Store;
using Content.Server._WG.Operations;
using Content.Server._WG.Remade.Conversion;
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
using Robust.Shared.Audio;
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
public sealed class WGFragmentRuleSystem : StationEventSystem<WGFragmentRuleComponent>
{
    [Dependency] private CurrencyInjectionSystem _currency = default!;
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private WGSiteGridSystem _siteGrid = default!;

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

        comp.NextWave = Timing.CurTime + comp.WaveInterval;
        Announce("wg-fragment-arrived", comp.Site, comp.ArrivedSound);
    }

    protected override void ActiveTick(EntityUid uid, WGFragmentRuleComponent comp, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, comp, gameRule, frameTime);

        if (comp.Cleared || comp.Grid is not { } grid || !Exists(grid) || Timing.CurTime < comp.NextCheck)
            return;
        comp.NextCheck = Timing.CurTime + TimeSpan.FromSeconds(1);

        comp.Remade.RemoveWhere(r => !Exists(r) || _mobState.IsDead(r));

        var heartPos = comp.HeartUid is { } living && !TerminatingOrDeleted(living)
            ? _transform.GetMapCoordinates(living)
            : MapCoordinates.Nullspace;
        var near = new List<Vector2>();
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var state, out var xform))
        {
            if (xform.GridUid != grid || _mobState.IsDead(player, state))
                continue;

            if (!comp.HeartBroken)
                RecordVisit(comp, player);

            var pos = _transform.GetWorldPosition(xform);
            if ((pos - heartPos.Position).Length() <= comp.WakeRadius)
                near.Add(pos);
        }

        // A heart gone without breaking pays nothing; the fragment just stops sending waves.
        if (heartPos == MapCoordinates.Nullspace)
            return;

        if (Timing.CurTime < comp.NextWave)
            return;
        comp.NextWave = Timing.CurTime + comp.WaveInterval;

        var size = WaveSize(near.Count, comp.Remade.Count, comp.WaveBase, comp.PlayersPerExtra, comp.MaxAlive);
        if (size == 0 || comp.Common.Count == 0)
            return;

        comp.Wave++;
        // The elite takes one of the wave's slots, so the wave never passes the cap.
        var elite = comp.Elites.Count > 0 && comp.EliteEvery > 0 && comp.Wave % comp.EliteEvery == 0;
        if (elite)
            size--;
        var spots = _siteGrid.OpenTiles((grid, Comp<MapGridComponent>(grid)));
        // Out of sight where possible: tiles at least 6 m from every player near the heart.
        var hidden = spots.Where(s =>
        {
            var world = _transform.ToMapCoordinates(s).Position;
            return near.All(p => (p - world).Length() >= 6f);
        }).ToList();
        if (hidden.Count > 0)
            spots = hidden;
        if (spots.Count == 0)
            return;

        for (var i = 0; i < size; i++)
            comp.Remade.Add(SpawnRemade(RobustRandom.Pick(comp.Common), RobustRandom.Pick(spots)));

        if (elite)
            comp.Remade.Add(SpawnRemade(RobustRandom.Pick(comp.Elites), RobustRandom.Pick(spots)));
    }

    protected override void Ended(EntityUid uid, WGFragmentRuleComponent comp, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, comp, gameRule, args);

        if (comp.Grid != null)
            Announce(comp.Cleared ? "wg-fragment-departed" : "wg-fragment-lost", comp.Site,
                comp.Cleared ? comp.DepartedSound : comp.LostSound);

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

        Announce(joint ? "wg-fragment-cleared-joint" : "wg-fragment-cleared", comp.Site, comp.ClearedSound);
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



    /// <summary>A sector-wide announcement with the fragment's position to the nearest 250 m.</summary>
    private void Announce(string key, MapCoordinates at, SoundSpecifier? sound)
    {
        var message = Loc.GetString(key,
            ("x", (int) MathF.Round(at.X / 250f) * 250),
            ("y", (int) MathF.Round(at.Y / 250f) * 250));
        ChatSystem.DispatchGlobalAnnouncement(message, Loc.GetString("wg-fragment-announcer"), announcementSound: sound, colorOverride: Color.MediumPurple);
    }
}
