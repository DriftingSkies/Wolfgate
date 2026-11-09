using System.Linq;
using Content.Server._WF.Remade.Corruption;
using Content.Shared._WF.Lattice;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Lattice;

/// <summary>
/// The Coda grows Remade flesh on ships that stay. Now and then a node takes root somewhere quiet aboard an unsheltered
/// crewed ship, and flesh creeps out from it across the floor and up the walls. Burn it or cut it back; kill the node
/// and its flesh dies. Out of the Coda nodes stop growing and wither away, and a drydock's Clean Drift strips them.
/// </summary>
public sealed class WFCodaFleshSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFCodaHazardSystem _hazards = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(6);

    /// <summary>How long a node out of the Coda takes to wither, at most.</summary>
    private const float WitherSeconds = 600f;

    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCodaFleshNodeComponent, EntityTerminatingEvent>(OnNodeGone);
    }

    /// <summary>A node that dies takes its flesh with it, over a minute.</summary>
    private void OnNodeGone(Entity<WFCodaFleshNodeComponent> ent, ref EntityTerminatingEvent args)
    {
        foreach (var tile in ent.Comp.Tiles)
        {
            _corruption.Wither(tile, 60f);
        }
    }

    /// <summary>Rolls whether flesh takes root on a ship this tick. Called by the Coda's hazards for each crewed ship there.</summary>
    public void Roll(Entity<MapGridComponent> grid, List<EntityUid> crew, float depth, WFLatticeProfilePrototype profile, float seconds)
    {
        if (Nodes(grid.Owner) >= profile.FleshMaxNodes
            || !_random.Prob(profile.FleshChance * (1f + depth) * seconds / 60f))
            return;

        Seed(grid, crew);
    }

    /// <summary>Puts a flesh node somewhere aboard away from the crew. False if there's nowhere.</summary>
    public bool Seed(Entity<MapGridComponent> grid, List<EntityUid> crew)
    {
        if (!_hazards.TryFindQuietFloor(grid, crew, out var at))
            return false;

        var node = Spawn("WFCodaFleshNode", at);
        var comp = EnsureComp<WFCodaFleshNodeComponent>(node);
        comp.Grid = grid.Owner;
        if (_corruption.GrowLoose(at) is { } first)
            comp.Tiles.Add(first);

        return true;
    }

    /// <summary>Strips every node and its flesh off a ship at once.</summary>
    public void Clear(EntityUid grid)
    {
        var nodes = EntityQueryEnumerator<WFCodaFleshNodeComponent>();
        while (nodes.MoveNext(out var uid, out var node))
        {
            if (node.Grid != grid)
                continue;

            foreach (var tile in node.Tiles)
            {
                if (!TerminatingOrDeleted(tile))
                    QueueDel(tile);
            }

            node.Tiles.Clear();
            QueueDel(uid);
        }
    }

    /// <summary>How many nodes are rooted on a ship.</summary>
    public int Nodes(EntityUid grid)
    {
        var count = 0;
        var nodes = EntityQueryEnumerator<WFCodaFleshNodeComponent>();
        while (nodes.MoveNext(out _, out var node))
        {
            if (node.Grid == grid)
                count++;
        }

        return count;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Tick;
        var nodes = EntityQueryEnumerator<WFCodaFleshNodeComponent, TransformComponent>();
        while (nodes.MoveNext(out var uid, out var node, out var xform))
        {
            node.Tiles.RemoveWhere(tile => TerminatingOrDeleted(tile));
            if (xform.GridUid != node.Grid || !TryComp<MapGridComponent>(node.Grid, out var grid))
            {
                QueueDel(uid);
                continue;
            }

            // Out of the Coda, a node stops growing and withers.
            if (!_hazards.IsExposed(node.Grid))
            {
                node.WitherAt ??= now + TimeSpan.FromSeconds(_random.NextFloat(WitherSeconds * 0.5f, WitherSeconds));
                if (now >= node.WitherAt)
                    QueueDel(uid);

                continue;
            }

            node.WitherAt = null;
            if (node.Tiles.Count < node.Capacity)
                Grow((node.Grid, grid), uid, node);
        }
    }

    /// <summary>Grows a tile or two of flesh at the edge of what a node already holds, within its reach.</summary>
    private void Grow(Entity<MapGridComponent> grid, EntityUid uid, WFCodaFleshNodeComponent node)
    {
        var centre = _maps.CoordinatesToTile(grid.Owner, grid.Comp, Transform(uid).Coordinates);
        var held = new HashSet<Vector2i>();
        foreach (var tile in node.Tiles)
        {
            held.Add(_maps.CoordinatesToTile(grid.Owner, grid.Comp, Transform(tile).Coordinates));
        }

        if (held.Count == 0)
            held.Add(centre);

        var frontier = new List<Vector2i>();
        foreach (var cell in held)
        {
            foreach (var step in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
            {
                var next = cell + step;
                var off = next - centre;
                if (held.Contains(next) || off.X * off.X + off.Y * off.Y > node.Reach * node.Reach)
                    continue;

                var tile = _maps.GetTileRef(grid.Owner, grid.Comp, next);
                if (tile.Tile.IsEmpty || _turf.IsTileBlocked(tile, CollisionGroup.Impassable))
                    continue;

                frontier.Add(next);
            }
        }

        if (frontier.Count == 0)
            return;

        var count = _random.Next(1, 3);
        foreach (var next in frontier.OrderBy(_ => _random.Next()).Take(count))
        {
            if (_corruption.GrowLoose(_maps.GridTileToLocal(grid.Owner, grid.Comp, next)) is { } grown)
                node.Tiles.Add(grown);
        }
    }
}

/// <summary>A knot of Remade flesh rooted on a ship in the Coda, feeding the flesh round it.</summary>
[RegisterComponent]
public sealed partial class WFCodaFleshNodeComponent : Component
{
    [ViewVariables]
    public EntityUid Grid;

    /// <summary>The flesh it is feeding.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Tiles = new();

    /// <summary>Most tiles it feeds.</summary>
    [DataField]
    public int Capacity = 40;

    /// <summary>Tiles from the node it reaches.</summary>
    [DataField]
    public float Reach = 6f;

    /// <summary>When it withers, once its ship has left the Coda.</summary>
    [ViewVariables]
    public TimeSpan? WitherAt;
}
