using System.Linq;
using System.Numerics;
using Content.Server._Mono.FireControl;
using Content.Server.Decals;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.Encounters;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Power.Components;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>Pirate retrofitting of a freshly spawned hull: random guns on its hardpoints, grime and rust through its decks.</summary>
public sealed partial class WFPirateRetrofitSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private FireControlSystem _fireControl = default!;
    [Dependency] private BatterySystem _battery = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";

    /// <summary>The run's random source: seeded, so the same hull with the same seed gets the same refit.</summary>
    private System.Random _rng = new();

    /// <summary>Retrofits a hull in one go. True if it mounted guns of its pool, false if it only dirtied the decks.</summary>
    public bool Apply(EntityUid grid, WFPirateRetrofitPrototype retrofit, int seed)
    {
        var run = Begin(seed);
        while (!Step(grid, retrofit, run))
        {
        }

        return run.Armed;
    }

    /// <summary>Starts a retrofit to run a step at a time with <see cref="Step"/>.</summary>
    public WFPirateRetrofitRun Begin(int seed)
    {
        return new WFPirateRetrofitRun(new System.Random(seed));
    }

    /// <summary>Runs a retrofit's next step: guns, rust, worn tiles, grime, cobwebs, then lights. True once it is done.</summary>
    public bool Step(EntityUid grid, WFPirateRetrofitPrototype retrofit, WFPirateRetrofitRun run)
    {
        if (run.Done || !TryComp<MapGridComponent>(grid, out var gridComp))
        {
            run.Step = WFPirateRetrofitRun.Steps;
            return true;
        }

        _rng = run.Rng;
        var ent = new Entity<MapGridComponent>(grid, gridComp);
        switch (run.Step)
        {
            case 0:
                run.Armed = retrofit.Guns.Count > 0 && Arm(ent, retrofit);
                break;
            case 1:
                Rust(ent, retrofit);
                break;
            case 2:
                run.Floors = Floors(ent);
                SwapTiles(ent, retrofit, run.Floors);
                break;
            case 3:
                Dirty(ent, retrofit, run.Floors!);
                break;
            case 4:
                Cobwebs(ent, retrofit, run.Floors!);
                break;
            default:
                LightsOut(grid, retrofit);
                break;
        }

        run.Step++;
        return run.Done;
    }

    /// <summary>
    /// Pulls the mapped guns its pool doesn't list, then fills the hardpoints at random from the pool up to a count it
    /// picks, each gun only where it fits and only while the ship's fire control has the processing power for it.
    /// </summary>
    private bool Arm(Entity<MapGridComponent> grid, WFPirateRetrofitPrototype retrofit)
    {
        var hardpoints = new List<(EntityUid Uid, HardpointComponent Comp)>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (TryComp<HardpointComponent>(uid, out var hardpoint) && Transform(uid).Anchored)
                hardpoints.Add((uid, hardpoint));
        }

        // In a fixed order before the shuffle, so the seed decides it and not the order entities came in.
        hardpoints.Sort((a, b) => Order(a.Uid, b.Uid));
        Shuffle(hardpoints);
        var target = Math.Min(_rng.Next(retrofit.MinGuns, Math.Max(retrofit.MinGuns, retrofit.MaxGuns) + 1), hardpoints.Count);
        var power = ProcessingPower(grid.Owner);
        var used = 0;
        var mounted = 0;
        var free = new List<(EntityUid Uid, HardpointComponent Comp)>();
        foreach (var hardpoint in hardpoints)
        {
            if (hardpoint.Comp.anchoring is not { } gun || TerminatingOrDeleted(gun))
            {
                free.Add(hardpoint);
                continue;
            }

            // A mapped gun stays only if it is in the pool and the count and fire control leave room for it.
            var cost = Cost(MetaData(gun).EntityPrototype?.ID);
            var listed = MetaData(gun).EntityPrototype is { } proto && retrofit.Guns.Contains(proto.ID);
            if (listed && mounted < target && used + cost <= power)
            {
                mounted++;
                used += cost;
                continue;
            }

            Del(gun);
            free.Add(hardpoint);
        }

        foreach (var hardpoint in free)
        {
            if (mounted >= target)
                break;

            var fits = retrofit.Guns.Where(gun => Fits(gun, hardpoint.Comp) && used + Cost(gun) <= power).ToList();
            if (fits.Count == 0)
                continue;

            var pick = Pick(fits);
            var xform = Transform(hardpoint.Uid);
            var spawned = Spawn(pick, xform.Coordinates);
            _transform.SetLocalRotation(spawned, xform.LocalRotation);
            mounted++;
            used += Cost(pick);
        }

        ChargeServers(grid.Owner);

        // A server not yet connected takes the guns itself once the ship has power.
        if (TryComp<FireControlGridComponent>(grid.Owner, out var control))
            _fireControl.RefreshControllables(grid.Owner, control);
        return true;
    }

    /// <summary>
    /// Fills the fire control servers' batteries. Hulls are saved with them empty, and an empty one only comes up once
    /// its APC has ramped up to its recharge draw, which left a fresh raider's guns dead for its first seconds.
    /// </summary>
    private void ChargeServers(EntityUid grid)
    {
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (HasComp<FireControlServerComponent>(uid) && TryComp<BatteryComponent>(uid, out var battery))
                _battery.SetCharge(uid, battery.MaxCharge, battery);
        }
    }

    /// <summary>Whether a gun's class and size suit a hardpoint.</summary>
    private bool Fits(EntProtoId gun, HardpointComponent hardpoint)
    {
        return _prototypes.TryIndex(gun, out var proto)
            && proto.TryComp<HardpointAnchorableOnlyComponent>(out var mount, EntityManager.ComponentFactory)
            && (hardpoint.CompatibleTypes & mount.CompatibleTypes) != 0
            && hardpoint.CompatibleSizes >= mount.CompatibleSizes;
    }

    /// <summary>The processing power a gun takes on fire control, as the fire control system counts it.</summary>
    private int Cost(string? gun)
    {
        if (gun == null || !_prototypes.TryIndex<EntityPrototype>(gun, out var proto)
            || !proto.TryComp<ShipGunClassComponent>(out var gunClass, EntityManager.ComponentFactory))
            return 0;

        return gunClass.Class switch
        {
            ShipGunClass.Superlight => 1,
            ShipGunClass.Light => 3,
            ShipGunClass.Medium => 6,
            ShipGunClass.Heavy => 9,
            ShipGunClass.Superheavy => 12,
            _ => 0,
        };
    }

    /// <summary>The processing power of the best fire control server aboard; none aboard is no limit, since nothing can fire anyway.</summary>
    private int ProcessingPower(EntityUid grid)
    {
        var best = -1;
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (TryComp<FireControlServerComponent>(uid, out var server))
                best = Math.Max(best, server.ProcessingPower);
        }

        return best < 0 ? int.MaxValue : best;
    }

    /// <summary>Swaps a share of the walls for their rusted version.</summary>
    private void Rust(Entity<MapGridComponent> grid, WFPirateRetrofitPrototype retrofit)
    {
        if (retrofit.WallSwaps.Count == 0 || retrofit.WallChance <= 0f)
            return;

        // Anchored walls are direct children of the grid, so only walk those rather than every tagged entity in the world.
        var swaps = new List<(EntityUid Wall, EntProtoId Rusted)>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (Transform(uid).Anchored && MetaData(uid).EntityPrototype is { } proto
                && retrofit.WallSwaps.TryGetValue(proto.ID, out var rusted))
            {
                swaps.Add((uid, rusted));
            }
        }

        swaps.Sort((a, b) => Order(a.Wall, b.Wall));
        foreach (var (wall, rusted) in swaps)
        {
            if (!Prob(retrofit.WallChance))
                continue;

            var xform = Transform(wall);
            var coordinates = xform.Coordinates;
            var rotation = xform.LocalRotation;
            Del(wall);
            var spawned = Spawn(rusted, coordinates);
            _transform.SetLocalRotation(spawned, rotation);
        }
    }

    /// <summary>The tiles of a grid with a floor and no wall on them.</summary>
    private List<TileRef> Floors(Entity<MapGridComponent> grid)
    {
        var floors = new List<TileRef>();
        var tiles = _map.GetAllTiles(grid.Owner, grid.Comp);
        while (tiles.MoveNext(out var tile))
        {
            if (!tile.Value.Tile.IsEmpty && !Walled(grid, tile.Value.GridIndices))
                floors.Add(tile.Value);
        }

        return floors;
    }

    private bool Walled(Entity<MapGridComponent> grid, Vector2i indices)
    {
        foreach (var anchored in _map.GetAnchoredEntities(grid, indices))
        {
            if (_tags.HasTag(anchored, WallTag))
                return true;
        }

        return false;
    }

    /// <summary>Swaps a share of each listed floor kind for a worn one, and at least one floor when any is listed.</summary>
    private void SwapTiles(Entity<MapGridComponent> grid, WFPirateRetrofitPrototype retrofit, List<TileRef> floors)
    {
        if (retrofit.TileSwaps.Count == 0)
            return;

        var changes = new List<(Vector2i, Tile)>();
        var candidates = new List<(TileRef Floor, WFPirateRetrofitTileSwap Swap)>();
        foreach (var floor in floors)
        {
            var id = _tileDefs[floor.Tile.TypeId].ID;
            foreach (var swap in retrofit.TileSwaps)
            {
                if (swap.To.Count == 0 || !swap.From.Contains(id))
                    continue;

                candidates.Add((floor, swap));
                if (!Prob(swap.Chance))
                    continue;

                var worn = (ContentTileDefinition) _tileDefs[Pick(swap.To)];
                changes.Add((floor.GridIndices, new Tile(worn.TileId)));
                break;
            }
        }

        // A hull with few listed floors can roll none; it still shows wear.
        if (changes.Count == 0 && candidates.Count > 0)
        {
            var (floor, swap) = Pick(candidates);
            var worn = (ContentTileDefinition) _tileDefs[Pick(swap.To)];
            changes.Add((floor.GridIndices, new Tile(worn.TileId)));
        }

        if (changes.Count > 0)
            _map.SetTiles(grid, changes);
    }

    /// <summary>Lays grime decals and litter across the floors.</summary>
    private void Dirty(Entity<MapGridComponent> grid, WFPirateRetrofitPrototype retrofit, List<TileRef> floors)
    {
        if (floors.Count == 0)
            return;

        var total = retrofit.Decals.Sum(decal => decal.Weight);
        var decals = total > 0f ? (int) (floors.Count * retrofit.DecalDensity) : 0;
        for (var i = 0; i < decals; i++)
        {
            var decal = PickDecal(retrofit.Decals, total);
            var tile = Pick(floors).GridIndices;
            var jitter = new Vector2(NextFloat(-0.2f, 0.2f), NextFloat(-0.2f, 0.2f));
            var at = new EntityCoordinates(grid.Owner, (Vector2) tile + jitter);
            _decals.TryAddDecal(decal.Id, at, out _, decal.Color, new Angle(NextFloat(0f, MathF.Tau)), cleanable: true);
        }

        if (retrofit.Litter.Count == 0)
            return;

        var litter = (int) (floors.Count * retrofit.LitterDensity);
        for (var i = 0; i < litter; i++)
        {
            Spawn(Pick(retrofit.Litter), _map.GridTileToLocal(grid.Owner, grid.Comp, Pick(floors).GridIndices));
        }
    }

    private WFPirateRetrofitDecal PickDecal(List<WFPirateRetrofitDecal> decals, float total)
    {
        var roll = NextFloat(0f, total);
        foreach (var decal in decals)
        {
            roll -= decal.Weight;
            if (roll <= 0f)
                return decal;
        }

        return decals[^1];
    }

    /// <summary>Hangs cobwebs in floor corners between two walls, turned to the corner.</summary>
    private void Cobwebs(Entity<MapGridComponent> grid, WFPirateRetrofitPrototype retrofit, List<TileRef> floors)
    {
        if (retrofit.CobwebChance <= 0f)
            return;

        foreach (var floor in floors)
        {
            var at = floor.GridIndices;
            var north = Walled(grid, at + Vector2i.Up);
            var south = Walled(grid, at + Vector2i.Down);
            var west = Walled(grid, at + Vector2i.Left);
            var east = Walled(grid, at + Vector2i.Right);
            (EntProtoId Web, Angle Turn)? corner = (north, south, west, east) switch
            {
                (true, _, true, _) => (retrofit.CobwebWest, Angle.Zero),
                (true, _, _, true) => (retrofit.CobwebEast, Angle.Zero),
                (_, true, _, true) => (retrofit.CobwebWest, Angle.FromDegrees(180)),
                (_, true, true, _) => (retrofit.CobwebEast, Angle.FromDegrees(180)),
                _ => null,
            };

            if (corner is not { } web || !Prob(retrofit.CobwebChance))
                continue;

            var spawned = Spawn(web.Web, _map.GridTileToLocal(grid.Owner, grid.Comp, at));
            _transform.SetLocalRotation(spawned, web.Turn);
        }
    }

    /// <summary>Takes the bulb out of a share of the light fixtures.</summary>
    private void LightsOut(EntityUid grid, WFPirateRetrofitPrototype retrofit)
    {
        if (retrofit.LightsOut <= 0f)
            return;

        var fixtures = new List<EntityUid>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (HasComp<PoweredLightComponent>(uid))
                fixtures.Add(uid);
        }

        fixtures.Sort(Order);
        var bulbs = new List<EntityUid>();
        foreach (var fixture in fixtures)
        {
            if (Prob(retrofit.LightsOut) && _containers.TryGetContainer(fixture, SharedPoweredLightSystem.LightBulbContainer, out var container))
                bulbs.AddRange(container.ContainedEntities);
        }

        foreach (var bulb in bulbs)
        {
            Del(bulb);
        }
    }

    /// <summary>Orders two entities by where they stand on their grid, then by what they are, so the order is the same each time the hull is spawned.</summary>
    private int Order(EntityUid a, EntityUid b)
    {
        var first = Transform(a).LocalPosition;
        var second = Transform(b).LocalPosition;
        var x = first.X.CompareTo(second.X);
        if (x != 0)
            return x;

        var y = first.Y.CompareTo(second.Y);
        return y != 0 ? y : string.CompareOrdinal(MetaData(a).EntityPrototype?.ID, MetaData(b).EntityPrototype?.ID);
    }

    private bool Prob(float chance)
    {
        return _rng.NextDouble() < chance;
    }

    private float NextFloat(float min, float max)
    {
        return min + (float) _rng.NextDouble() * (max - min);
    }

    private T Pick<T>(IReadOnlyList<T> list)
    {
        return list[_rng.Next(list.Count)];
    }

    private void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>A retrofit run a step at a time: its random source, how far it has got and what it found.</summary>
public sealed class WFPirateRetrofitRun
{
    public const int Steps = 6;

    internal readonly System.Random Rng;
    internal int Step;
    internal List<TileRef>? Floors;

    /// <summary>Whether it mounted guns of its pool.</summary>
    public bool Armed { get; internal set; }

    public bool Done => Step >= Steps;

    internal WFPirateRetrofitRun(System.Random rng)
    {
        Rng = rng;
    }
}
