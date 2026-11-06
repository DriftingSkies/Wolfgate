using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lays out <see cref="FaunaBlastPatternComponent"/> patterns, strikes <see cref="FaunaBlastTileComponent"/> tiles and
/// casts patterns for <see cref="FaunaBlastCasterComponent"/> mobs.
/// </summary>
public sealed partial class FaunaBlastSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedContainerSystem _container = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    private static readonly Vector2i[] Cardinals = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
    private static readonly Vector2i[] Diagonals = { new(1, 1), new(-1, 1), new(1, -1), new(-1, -1) };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaBlastPatternComponent, MapInitEvent>(OnPatternInit);
        SubscribeLocalEvent<FaunaBlastTileComponent, MapInitEvent>(OnTileInit);
        SubscribeLocalEvent<FaunaBlastCasterComponent, FaunaBlastActionEvent>(OnBlastAction);
    }

    private void OnBlastAction(Entity<FaunaBlastCasterComponent> ent, ref FaunaBlastActionEvent args)
    {
        if (args.Handled || TerminatingOrDeleted(args.Target) || _container.IsEntityInContainer(args.Target))
            return;

        var coords = _transform.GetMapCoordinates(args.Target);
        if (coords.MapId != Transform(ent).MapID)
            return;

        args.Handled = true;
        Spawn(args.Pattern, coords);
    }

    private void OnTileInit(Entity<FaunaBlastTileComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.StrikeAt = _timing.CurTime + ent.Comp.Delay;
    }

    private void OnPatternInit(Entity<FaunaBlastPatternComponent> ent, ref MapInitEvent args)
    {
        var now = _timing.CurTime;
        var comp = ent.Comp;
        var cross = comp.Cross;
        if (cross == FaunaBlastCross.Random)
            cross = _random.Prob(comp.DiagonalChance) ? FaunaBlastCross.Diagonal : FaunaBlastCross.Cardinal;

        var telegraph = comp.Shape != FaunaBlastShape.Cross ? comp.Telegraph : cross switch
        {
            FaunaBlastCross.Cardinal => comp.TelegraphCardinal,
            FaunaBlastCross.Diagonal => comp.TelegraphDiagonal,
            _ => comp.Telegraph,
        };

        if (telegraph != null)
            Spawn(telegraph, Transform(ent).Coordinates);

        switch (comp.Shape)
        {
            case FaunaBlastShape.Square:
                for (var x = -comp.Radius; x <= comp.Radius; x++)
                {
                    for (var y = -comp.Radius; y <= comp.Radius; y++)
                    {
                        var ring = Math.Max(Math.Abs(x), Math.Abs(y));
                        comp.Pending.Add((now + comp.RingDelay * ring, new Vector2i(x, y)));
                    }
                }
                break;
            case FaunaBlastShape.Ring:
                for (var x = -comp.Radius; x <= comp.Radius; x++)
                {
                    for (var y = -comp.Radius; y <= comp.Radius; y++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Abs(y)) == comp.Radius)
                            comp.Pending.Add((now, new Vector2i(x, y)));
                    }
                }
                break;
            case FaunaBlastShape.Cross:
                comp.Pending.Add((now, Vector2i.Zero));
                foreach (var dir in CrossDirections(cross))
                {
                    for (var i = 1; i <= comp.Radius; i++)
                    {
                        comp.Pending.Add((now, dir * i));
                    }
                }
                break;
        }
    }

    private static IEnumerable<Vector2i> CrossDirections(FaunaBlastCross cross)
    {
        if (cross != FaunaBlastCross.Diagonal)
        {
            foreach (var dir in Cardinals)
            {
                yield return dir;
            }
        }

        if (cross != FaunaBlastCross.Cardinal)
        {
            foreach (var dir in Diagonals)
            {
                yield return dir;
            }
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var patterns = EntityQueryEnumerator<FaunaBlastPatternComponent, TransformComponent>();
        while (patterns.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
            {
                QueueDel(uid);
                continue;
            }

            var origin = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            for (var i = comp.Pending.Count - 1; i >= 0; i--)
            {
                var (at, offset) = comp.Pending[i];
                if (now < at)
                    continue;

                comp.Pending.RemoveAt(i);
                var tile = origin + offset;
                if (!_map.TryGetTileRef(grid, gridComp, tile, out var tileRef) || tileRef.Tile.IsEmpty
                    || _turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable, gridComp))
                    continue;

                Spawn(comp.Effect, _map.GridTileToLocal(grid, gridComp, tile));
            }

            if (comp.Pending.Count == 0)
                QueueDel(uid);
        }

        var tiles = EntityQueryEnumerator<FaunaBlastTileComponent, TransformComponent>();
        while (tiles.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.Struck || now < comp.StrikeAt)
                continue;

            comp.Struck = true;
            if (comp.Damage == null)
                continue;

            _mobs.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, 0.45f, _mobs);
            foreach (var mob in _mobs)
            {
                if (HasComp<FaunaBlastCasterComponent>(mob) || !_mobState.IsAlive(mob, mob.Comp))
                    continue;

                _damageable.TryChangeDamage(mob.Owner, comp.Damage);
                _audio.PlayPvs(comp.HitSound, mob);
            }
        }
    }
}
