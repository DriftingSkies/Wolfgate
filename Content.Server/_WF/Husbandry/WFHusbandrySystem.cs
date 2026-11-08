using System.Linq;
using Content.Server._WF.Fauna;
using Content.Server._WF.Husbandry.Components;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server.Animals.Components;
using Content.Server.Cargo.Systems;
using Content.Shared.Animals;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Husbandry;

/// <summary>Pens round animal feeders on outposts, and the livestock kept in them through the outpost's saves.</summary>
public sealed partial class WFHusbandrySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private WFRaidWindowSystem _raidWindow = default!;

    /// <summary>The faction livestock is put in: nothing is hostile to it, and it to nothing.</summary>
    public const string LivestockFaction = "Passive";

    private static readonly TimeSpan PenInterval = TimeSpan.FromSeconds(5);

    private static readonly Vector2i[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>The faction livestock left outside a pen in an Active window is put in: whatever hunts by default hunts it.</summary>
    public const string PreyFaction = "WFLivestockPrey";

    private TimeSpan _nextPens;

    private readonly HashSet<EntityUid> _penned = new();
    private readonly Dictionary<EntityUid, bool> _windows = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFOutpostComponent, WFOutpostAppraiseEvent>(OnAppraise);
        SubscribeLocalEvent<WFOutpostComponent, WFOutpostFingerprintEvent>(OnFingerprint);
        InitializeRecords();
        InitializeLasso();
        InitializeTaming();
        InitializeHerding();
    }

    /// <summary>
    /// Lets go of lassos stretched too far, makes the animals standing in an outpost's pens its livestock, and leaves
    /// livestock outside a pen to predators while its world's Active window is open.
    /// </summary>
    public override void Update(float frameTime)
    {
        UpdateLassos();
        if (_timing.CurTime < _nextPens)
            return;

        _nextPens = _timing.CurTime + PenInterval;
        _penned.Clear();
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out _, out var grid))
        {
            foreach (var pen in GetPens((uid, grid)))
            {
                foreach (var animal in pen.Livestock)
                {
                    MakeLivestock(animal);
                    _penned.Add(animal);
                }
            }
        }

        UpdateExposure();
    }

    /// <summary>Puts livestock outside a pen in the prey faction while its world's Active window is open, and back after.</summary>
    private void UpdateExposure()
    {
        _windows.Clear();
        var livestock = EntityQueryEnumerator<WFLivestockComponent, TransformComponent>();
        while (livestock.MoveNext(out var uid, out var comp, out var xform))
        {
            var exposed = false;
            if (!_penned.Contains(uid) && xform.MapUid is { } map)
            {
                if (!_windows.TryGetValue(map, out exposed))
                {
                    exposed = _raidWindow.TryGetWindowOn(map, out var active, out _) && active;
                    _windows[map] = exposed;
                }
            }

            if (exposed == comp.Exposed)
                continue;

            comp.Exposed = exposed;
            if (!HasComp<NpcFactionMemberComponent>(uid))
                continue;

            _faction.ClearFactions(uid, false);
            _faction.AddFaction(uid, exposed ? PreyFaction : LivestockFaction);
        }
    }

    /// <summary>
    /// The pens on a grid: the floor round each anchored feeder that walls, fences or other feeders close off before it
    /// runs out of floor or past the feeder's limit, with the animals in it up to its feeders' capacity.
    /// </summary>
    public List<WFPen> GetPens(Entity<MapGridComponent> grid)
    {
        var pens = new List<WFPen>();
        var feeders = new List<Entity<WFAnimalFeederComponent>>();
        var animals = new List<(EntityUid Uid, Vector2i Tile)>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            var xform = Transform(child);
            if (TryComp<WFAnimalFeederComponent>(child, out var feeder))
            {
                if (xform.Anchored)
                    feeders.Add((child, feeder));
            }
            else if (IsLivestock(child))
            {
                animals.Add((child, _maps.LocalToTile(grid, grid, xform.Coordinates)));
            }
        }

        var seen = new HashSet<EntityUid>();
        foreach (var feeder in feeders)
        {
            if (!seen.Add(feeder))
                continue;

            var pen = new WFPen();
            if (!TryFlood(grid, feeder, pen))
                continue;

            seen.UnionWith(pen.Feeders.Select(f => f.Owner));
            pens.Add(pen);
        }

        animals.Sort((a, b) => a.Uid.CompareTo(b.Uid));
        foreach (var (animal, tile) in animals)
        {
            foreach (var pen in pens)
            {
                if (!pen.Tiles.Contains(tile))
                    continue;

                if (pen.Livestock.Count < pen.Capacity)
                    pen.Livestock.Add(animal);
                break;
            }
        }

        return pens;
    }

    /// <summary>
    /// Floods out from a feeder over floor nothing solid stands on, taking in other feeders it meets. False when the
    /// floor runs out or past the feeder's limit, so the space isn't enclosed.
    /// </summary>
    private bool TryFlood(Entity<MapGridComponent> grid, Entity<WFAnimalFeederComponent> start, WFPen pen)
    {
        var origin = _maps.LocalToTile(grid, grid, Transform(start).Coordinates);
        pen.Tiles.Add(origin);
        pen.Feeders.Add(start);
        pen.FeederTiles.Add(origin);

        var pending = new Queue<Vector2i>();
        pending.Enqueue(origin);
        while (pending.TryDequeue(out var tile))
        {
            foreach (var step in Neighbours)
            {
                var next = tile + step;
                if (pen.Tiles.Contains(next))
                    continue;

                if (!_maps.TryGetTileRef(grid, grid, next, out var tileRef) || tileRef.Tile.IsEmpty)
                    return false;

                if (FeederAt(grid, next) is { } other)
                {
                    pen.Tiles.Add(next);
                    pen.Feeders.Add(other);
                    pen.FeederTiles.Add(next);
                    continue;
                }

                if (_turf.IsTileBlocked(grid, next, CollisionGroup.MobMask, grid.Comp, minIntersectionArea: 0.01f))
                    continue;

                pen.Tiles.Add(next);
                pen.Open.Add(next);
                if (pen.Tiles.Count > start.Comp.MaxPenTiles)
                    return false;

                pending.Enqueue(next);
            }
        }

        return true;
    }

    private Entity<WFAnimalFeederComponent>? FeederAt(Entity<MapGridComponent> grid, Vector2i tile)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(grid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (TryComp<WFAnimalFeederComponent>(uid, out var feeder))
                return (uid.Value, feeder);
        }

        return null;
    }

    /// <summary>Whether an animal can be kept as livestock: alive, nobody's body, and livestock or a producer of eggs, milk or wool.</summary>
    public bool IsLivestock(EntityUid uid)
    {
        if (HasComp<ActorComponent>(uid) || !TryComp<MobStateComponent>(uid, out var state) || !_mobState.IsAlive(uid, state))
            return false;

        return HasComp<WFLivestockComponent>(uid)
               || HasComp<EggLayerComponent>(uid)
               || HasComp<UdderComponent>(uid)
               || HasComp<WoolyComponent>(uid);
    }

    /// <summary>
    /// Makes an animal livestock, unless it already is: aged from now, passive, herdable, kept from retiring as wildlife
    /// and from being pulled back to where it spawned.
    /// </summary>
    public Entity<WFLivestockComponent> MakeLivestock(EntityUid uid)
    {
        if (TryComp<WFLivestockComponent>(uid, out var existing))
            return (uid, existing);

        var livestock = AddComp<WFLivestockComponent>(uid);
        livestock.Since = _timing.CurTime;
        if (HasComp<NpcFactionMemberComponent>(uid))
        {
            _faction.ClearFactions(uid, false);
            _faction.AddFaction(uid, LivestockFaction);
        }

        if (TryComp<WFPlanetWildlifeComponent>(uid, out var wildlife))
            wildlife.Protected = true;
        RemComp<FaunaLeashComponent>(uid);
        SetLivestockTask(uid);
        return (uid, livestock);
    }

    /// <summary>How long an animal has been kept, through every save it came back from.</summary>
    public TimeSpan GetAge(Entity<WFLivestockComponent> livestock)
    {
        return livestock.Comp.Age + (_timing.CurTime - livestock.Comp.Since);
    }

    private void OnAppraise(Entity<WFOutpostComponent> outpost, ref WFOutpostAppraiseEvent args)
    {
        if (TryComp<WFLivestockRecordsComponent>(outpost, out var records))
            args.Value += records.Records.Sum(r => r.Price);

        if (!TryComp<MapGridComponent>(outpost, out var grid))
            return;

        foreach (var pen in GetPens((outpost, grid)))
        {
            foreach (var animal in pen.Livestock)
            {
                args.Value += _pricing.GetPrice(animal);
            }
        }
    }

    private void OnFingerprint(Entity<WFOutpostComponent> outpost, ref WFOutpostFingerprintEvent args)
    {
        if (!TryComp<MapGridComponent>(outpost, out var grid))
            return;

        var hash = new HashCode();
        hash.Add(args.Hash);
        foreach (var pen in GetPens((outpost, grid)))
        {
            foreach (var animal in pen.Livestock)
            {
                hash.Add(animal);
            }
        }

        args.Hash = hash.ToHashCode();
    }
}

/// <summary>A pen on a grid: its floor, the feeders in it and the livestock it keeps.</summary>
public sealed class WFPen
{
    public readonly HashSet<Vector2i> Tiles = new();

    /// <summary>The pen's floor without its feeders, where animals stand.</summary>
    public readonly List<Vector2i> Open = new();

    public readonly List<Entity<WFAnimalFeederComponent>> Feeders = new();

    public readonly List<Vector2i> FeederTiles = new();

    public readonly List<EntityUid> Livestock = new();

    /// <summary>How many animals the pen keeps: its feeders' capacity together.</summary>
    public int Capacity => Feeders.Sum(f => f.Comp.Capacity);
}
