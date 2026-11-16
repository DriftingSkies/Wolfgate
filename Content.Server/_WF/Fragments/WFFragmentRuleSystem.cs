using System.Linq;
using System.Numerics;
using Content.Server._Mono.Store;
using Content.Server._WF.Remade.Conversion;
using Content.Server.StationEvents.Events;
using Content.Shared._Mono.Company;
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

namespace Content.Server._WF.Fragments;

/// <summary>
/// Places a Monolith fragment about as far from each faction base as the other, swaps its mobs for Remade, sends waves
/// from its heart at whoever comes near, pays every faction when the heart breaks and removes the fragment at the end.
/// </summary>
public sealed class WFFragmentRuleSystem : StationEventSystem<WFFragmentRuleComponent>
{
    [Dependency] private CurrencyInjectionSystem _currency = default!;
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private TurfSystem _turf = default!;

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

    protected override void Started(EntityUid uid, WFFragmentRuleComponent comp, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, gameRule, args);

        var mapId = GameTicker.DefaultMap;
        if (!MapSystem.MapExists(mapId) || comp.Grids.Count == 0)
            return;

        var site = PickSite(comp, mapId);
        var path = RobustRandom.Pick(comp.Grids);
        if (!_loader.TryLoadGrid(mapId, path, out var grid, offset: site))
        {
            Log.Error($"Fragment failed to load {path}");
            return;
        }

        comp.Grid = grid.Value;
        comp.Site = new MapCoordinates(site, mapId);
        _metaData.SetEntityName(grid.Value, Loc.GetString("wf-fragment-grid-name"));

        foreach (var child in Children(grid.Value))
        {
            if (MetaData(child).EntityPrototype is not { } proto
                || !comp.Replace.TryGetValue(proto.ID, out var pool)
                || pool.Count == 0)
                continue;

            var coords = Transform(child).Coordinates;
            Del(child);
            comp.Remade.Add(SpawnRemade(RobustRandom.Pick(pool), coords));
        }

        var tiles = OpenTiles(grid.Value);
        if (tiles.Count == 0)
            tiles.Add(new EntityCoordinates(grid.Value, Vector2.Zero));

        // The heart sits on the open tile nearest the fragment's middle.
        var centre = tiles.Aggregate(Vector2.Zero, (sum, t) => sum + t.Position) / tiles.Count;
        var heartAt = tiles.MinBy(t => (t.Position - centre).LengthSquared());
        comp.HeartUid = Spawn(comp.Heart, heartAt);

        for (var i = 0; i < comp.DecorCount && comp.Decor.Count > 0; i++)
            Spawn(RobustRandom.Pick(comp.Decor), RobustRandom.Pick(tiles));

        comp.NextWave = Timing.CurTime + comp.WaveInterval;
        Announce("wf-fragment-arrived", comp.Site);
    }

    protected override void ActiveTick(EntityUid uid, WFFragmentRuleComponent comp, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, comp, gameRule, frameTime);

        if (comp.Cleared || comp.Grid is not { } grid || !Exists(grid) || Timing.CurTime < comp.NextCheck)
            return;
        comp.NextCheck = Timing.CurTime + TimeSpan.FromSeconds(1);

        comp.Remade.RemoveWhere(r => !Exists(r) || _mobState.IsDead(r));

        if (comp.HeartUid is not { } heart || TerminatingOrDeleted(heart))
        {
            Clear(comp);
            return;
        }

        var heartPos = _transform.GetMapCoordinates(heart);
        var near = new List<Vector2>();
        var players = EntityQueryEnumerator<ActorComponent, MobStateComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var state, out var xform))
        {
            if (xform.GridUid != grid || _mobState.IsDead(player, state))
                continue;

            if (TryComp<CompanyComponent>(player, out var company) && comp.Rewards.ContainsKey(company.CompanyName))
                comp.Present.Add(company.CompanyName);

            var pos = _transform.GetWorldPosition(xform);
            if ((pos - heartPos.Position).Length() <= comp.WakeRadius)
                near.Add(pos);
        }

        if (Timing.CurTime < comp.NextWave)
            return;
        comp.NextWave = Timing.CurTime + comp.WaveInterval;

        var size = WaveSize(near.Count, comp.Remade.Count, comp.WaveBase, comp.PlayersPerExtra, comp.MaxAlive);
        if (size == 0 || comp.Common.Count == 0)
            return;

        comp.Wave++;
        var spots = OpenTiles((grid, Comp<MapGridComponent>(grid)));
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

        if (comp.Elites.Count > 0 && comp.EliteEvery > 0 && comp.Wave % comp.EliteEvery == 0)
            comp.Remade.Add(SpawnRemade(RobustRandom.Pick(comp.Elites), RobustRandom.Pick(spots)));
    }

    protected override void Ended(EntityUid uid, WFFragmentRuleComponent comp, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, comp, gameRule, args);

        if (comp.Grid != null)
            Announce(comp.Cleared ? "wf-fragment-departed" : "wf-fragment-lost", comp.Site);

        if (comp.Grid is { } grid && Exists(grid))
            _lifecycle.UnparentPlayersFromGrid(grid, true);
        comp.Grid = null;
    }

    /// <summary>Pays every faction for the broken heart, with the joint bonus if each had members aboard.</summary>
    private void Clear(WFFragmentRuleComponent comp)
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

        Announce(joint ? "wf-fragment-cleared-joint" : "wf-fragment-cleared", comp.Site);
    }

    /// <summary>
    /// A point roughly as far from every faction base as from the others, at least the clearance from any of them;
    /// a random point near the sector's middle if the bases can't be found.
    /// </summary>
    private Vector2 PickSite(WFFragmentRuleComponent comp, MapId mapId)
    {
        var bases = new List<Vector2>();
        var uplinks = EntityQueryEnumerator<TagComponent, TransformComponent>();
        while (uplinks.MoveNext(out var uplink, out var tag, out var xform))
        {
            if (xform.MapID == mapId && comp.Bases.Any(b => _tags.HasTag(tag, b)))
                bases.Add(_transform.GetWorldPosition(xform));
        }

        if (bases.Count < 2)
            return RobustRandom.NextVector2(2000f, 4000f);

        var a = bases[0];
        var b = bases.MaxBy(p => (p - a).LengthSquared());
        var mid = (a + b) / 2f;
        var axis = b - a;
        var half = axis.Length() / 2f;
        var along = half > 0 ? axis / (half * 2f) : Vector2.UnitX;
        var across = new Vector2(-along.Y, along.X);

        // Along the line that splits the two bases, nudged at most a tenth of the way towards either.
        var minAcross = MathF.Sqrt(MathF.Max(0f, comp.BaseClearance * comp.BaseClearance - half * half));
        var maxAcross = MathF.Sqrt(MathF.Max(0f, comp.MaxDistance * comp.MaxDistance - half * half));
        var best = mid + across * MathF.Max(minAcross, comp.BaseClearance);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var side = RobustRandom.Prob(0.5f) ? 1f : -1f;
            var point = mid
                        + across * side * RobustRandom.NextFloat(minAcross, MathF.Max(minAcross, maxAcross))
                        + along * RobustRandom.NextFloat(-0.1f, 0.1f) * half;
            if (bases.All(p => (p - point).Length() >= comp.BaseClearance))
                return point;
        }
        return best;
    }

    /// <summary>A Remade answering to no Monolith: it never starves, and nothing it kills turns.</summary>
    private EntityUid SpawnRemade(EntProtoId proto, EntityCoordinates at)
    {
        var remade = Spawn(proto, at);
        EnsureComp<WFRemadeComponent>(remade);
        return remade;
    }

    /// <summary>The entities parented to the grid, copied so the caller may change them.</summary>
    private List<EntityUid> Children(EntityUid grid)
    {
        var children = new List<EntityUid>();
        var enumerator = Transform(grid).ChildEnumerator;
        while (enumerator.MoveNext(out var child))
            children.Add(child);
        return children;
    }

    /// <summary>Floor tiles on the grid that nothing blocks.</summary>
    private List<EntityCoordinates> OpenTiles(Entity<MapGridComponent> grid)
    {
        var tiles = new List<EntityCoordinates>();
        foreach (var tile in MapSystem.GetAllTiles(grid, grid.Comp))
        {
            if (_turf.IsSpace(tile) || _turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                continue;
            tiles.Add(MapSystem.GridTileToLocal(grid, grid.Comp, tile.GridIndices));
        }
        return tiles;
    }

    /// <summary>A sector-wide announcement with the fragment's position to the nearest 250 m.</summary>
    private void Announce(string key, MapCoordinates at)
    {
        var message = Loc.GetString(key,
            ("x", (int) MathF.Round(at.X / 250f) * 250),
            ("y", (int) MathF.Round(at.Y / 250f) * 250));
        ChatSystem.DispatchGlobalAnnouncement(message, Loc.GetString("wf-fragment-announcer"), colorOverride: Color.MediumPurple);
    }
}
