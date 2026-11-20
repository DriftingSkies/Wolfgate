using System.Linq;
using Content.Server.Atmos.Components;
using Content.Shared._WG.Augments;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Maps;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._WG.Augments;

/// <summary>
/// Casts a holo projector's copies of its body around the room it stands in, swaps the body into the place of one,
/// and pops a copy that is struck.
/// </summary>
public sealed partial class WGHoloProjectorSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MovementSpeedModifierSystem _speed = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    /// <summary>What a tile holding stops a cast: walls, machines, counters and closed doors.</summary>
    private const CollisionGroup Blocking = CollisionGroup.Impassable | CollisionGroup.MidImpassable
        | CollisionGroup.HighImpassable;

    private static readonly Vector2i[] Neighbours = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    private EntityQuery<AirtightComponent> _airtightQuery;
    private EntityQuery<DoorComponent> _doorQuery;

    public override void Initialize()
    {
        base.Initialize();
        _airtightQuery = GetEntityQuery<AirtightComponent>();
        _doorQuery = GetEntityQuery<DoorComponent>();

        SubscribeLocalEvent<WGHoloProjectorComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WGHoloProjectorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WGHoloProjectorComponent, WGHoloProjectEvent>(OnProject);
        SubscribeLocalEvent<WGHoloDecoyComponent, DamageChangedEvent>(OnDecoyDamaged);
    }

    private void OnInit(Entity<WGHoloProjectorComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnShutdown(Entity<WGHoloProjectorComponent> ent, ref ComponentShutdown args)
    {
        QueueDel(ent.Comp.ActionEntity);
        ent.Comp.ActionEntity = null;
    }

    private void OnProject(Entity<WGHoloProjectorComponent> ent, ref WGHoloProjectEvent args)
    {
        if (args.Handled)
            return;

        // A failed cast leaves the action unhandled, so it costs no cooldown.
        if (!TryCast(ent))
        {
            _popup.PopupEntity(Loc.GetString("wg-holo-projector-cramped"), ent, ent);
            return;
        }

        args.Handled = true;
    }

    /// <summary>
    /// Casts the copies and swaps the body into the place of one. Fails, changing nothing, where the body isn't
    /// standing free in a room with space to land in.
    /// </summary>
    public bool TryCast(Entity<WGHoloProjectorComponent> ent)
    {
        var (uid, comp) = ent;
        var xform = Transform(uid);
        if (_container.IsEntityInContainer(uid)
            || xform.GridUid is not { } grid
            || !TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var origin = _transform.WithEntityId(xform.Coordinates, grid);
        var start = _map.TileIndicesFor(grid, gridComp, origin);
        if (!Open(grid, gridComp, start))
            return false;

        // Land the body two or more steps away if the room has space, else on any other tile.
        var room = RoomTiles(grid, gridComp, start, comp.Range);
        var spots = room.Where(t => t.Steps >= 2).Select(t => t.Tile).ToList();
        if (spots.Count == 0)
            spots = room.Where(t => t.Steps >= 1).Select(t => t.Tile).ToList();
        if (spots.Count == 0)
            return false;

        if (_buckle.IsBuckled(uid))
            _buckle.Unbuckle((uid, null), uid);
        if (TryComp<PullableComponent>(uid, out var pullable) && _pulling.IsPulled(uid, pullable))
            _pulling.TryStopPull(uid, pullable);
        if (TryComp<PullerComponent>(uid, out var puller)
            && TryComp(puller.Pulling, out PullableComponent? pulled))
            _pulling.TryStopPull(puller.Pulling.Value, pulled);

        var rotation = _transform.GetWorldRotation(uid);
        var landing = _random.PickAndTake(spots);
        _transform.SetCoordinates(uid, _map.GridTileToLocal(grid, gridComp, landing));
        _transform.SetWorldRotation(uid, rotation);
        SpawnAtPosition(comp.Flicker, Transform(uid).Coordinates);
        _audio.PlayPvs(comp.CastSound, origin);

        // The first copy takes the body's old place; the rest land elsewhere, sharing a tile only if the room is full.
        for (var i = 0; i < comp.Copies; i++)
        {
            var at = i == 0 ? origin
                : _map.GridTileToLocal(grid, gridComp, spots.Count > 0 ? _random.PickAndTake(spots) : landing);
            SpawnDecoy(ent, at, rotation);
        }

        return true;
    }

    private void SpawnDecoy(Entity<WGHoloProjectorComponent> ent, EntityCoordinates at, Angle rotation)
    {
        var decoy = SpawnAtPosition(ent.Comp.Decoy, at);
        _transform.SetWorldRotation(decoy, rotation);
        SpawnAtPosition(ent.Comp.Flicker, at);

        var comp = EnsureComp<WGHoloDecoyComponent>(decoy);
        comp.Source = ent;
        Dirty(decoy, comp);
        _meta.SetEntityName(decoy, Identity.Name(ent, EntityManager));
        _meta.SetEntityDescription(decoy, Description(ent));

        // It walks at its source's pace, slowdowns and all.
        if (TryComp<MovementSpeedModifierComponent>(ent, out var move))
            _speed.ChangeBaseSpeed(decoy, move.CurrentWalkSpeed, move.CurrentSprintSpeed, move.Acceleration);
    }

    /// <summary>The tiles reachable from <paramref name="start"/> within <paramref name="range"/> steps without
    /// passing a wall, door or other blocking tile, with how many steps each took.</summary>
    private List<(Vector2i Tile, int Steps)> RoomTiles(EntityUid grid, MapGridComponent gridComp, Vector2i start, int range)
    {
        var found = new List<(Vector2i Tile, int Steps)>();
        var seen = new HashSet<Vector2i> { start };
        var queue = new Queue<(Vector2i Tile, int Steps)>();
        queue.Enqueue((start, 0));

        while (queue.TryDequeue(out var cur))
        {
            found.Add(cur);
            if (cur.Steps >= range)
                continue;

            foreach (var offset in Neighbours)
            {
                var next = cur.Tile + offset;
                if (seen.Add(next) && Open(grid, gridComp, next))
                    queue.Enqueue((next, cur.Steps + 1));
            }
        }

        return found;
    }

    /// <summary>Whether a tile is floor a body can stand on and no room boundary: no wall, window, door or space.</summary>
    private bool Open(EntityUid grid, MapGridComponent gridComp, Vector2i tile)
    {
        if (!_map.TryGetTileRef(grid, gridComp, tile, out var tileRef) || _turf.IsSpace(tileRef))
            return false;

        // An open door is still the way out of the room.
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (anchored.MoveNext(out var anchoredUid))
        {
            if (_doorQuery.HasComp(anchoredUid) || _airtightQuery.HasComp(anchoredUid))
                return false;
        }

        return !_turf.IsTileBlocked(grid, tile, Blocking, gridComp);
    }

    /// <summary>A copy that is struck flickers out.</summary>
    private void OnDecoyDamaged(Entity<WGHoloDecoyComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || TerminatingOrDeleted(ent))
            return;

        SpawnAtPosition(ent.Comp.Puff, Transform(ent).Coordinates);
        QueueDel(ent);
    }
}
