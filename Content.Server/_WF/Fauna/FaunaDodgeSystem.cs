using System.Numerics;
using Content.Server.Popups;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Projectiles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Decides once per projectile whether a <see cref="FaunaDodgeComponent"/> mob lets it pass. Side-steps wait for the
/// next tick, since the dodge is decided inside a physics step.
/// </summary>
public sealed partial class FaunaDodgeSystem : EntitySystem
{
    private const int MaxSeen = 32;

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;

    private readonly List<(EntityUid Mob, Vector2 Direction)> _pending = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaDodgeComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<FaunaDodgeComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled || !HasComp<ProjectileComponent>(args.OtherEntity) || !_mobState.IsAlive(ent))
            return;

        if (!ent.Comp.Seen.TryGetValue(args.OtherEntity, out var dodged))
        {
            if (ent.Comp.Seen.Count >= MaxSeen)
                ent.Comp.Seen.Clear();

            dodged = _random.Prob(Math.Clamp(ent.Comp.Pool / ent.Comp.MaxPool, 0f, 1f));
            ent.Comp.Seen[args.OtherEntity] = dodged;

            if (dodged)
            {
                _popup.PopupEntity(Loc.GetString(ent.Comp.Popup, ("user", ent.Owner)), ent);
                _audio.PlayPvs(ent.Comp.Sound, ent);
                if (_random.Prob(ent.Comp.UseChance))
                {
                    ent.Comp.Pool = Math.Max(0f, ent.Comp.Pool - ent.Comp.Usage);
                    if (ent.Comp.SideStep)
                        _pending.Add((ent, args.OtherBody.LinearVelocity));
                }
            }
        }

        if (dodged)
            args.Cancelled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pending.Count > 0)
        {
            var pending = _pending.ToArray();
            _pending.Clear();
            foreach (var (mob, direction) in pending)
            {
                SideStep(mob, direction);
            }
        }

        var query = EntityQueryEnumerator<FaunaDodgeComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Pool < comp.MaxPool && _mobState.IsAlive(uid))
                comp.Pool = Math.Min(comp.MaxPool, comp.Pool + comp.Regen * frameTime);
        }
    }

    /// <summary>
    /// Moves the mob one tile to a free side of the projectile's path.
    /// </summary>
    private void SideStep(EntityUid mob, Vector2 direction)
    {
        if (TerminatingOrDeleted(mob) || !_mobState.IsAlive(mob) || _container.IsEntityInContainer(mob) ||
            direction.LengthSquared() < 0.01f)
            return;

        var xform = Transform(mob);
        if (!TryComp<MapGridComponent>(xform.ParentUid, out var grid))
            return;

        var tile = _map.TileIndicesFor(xform.ParentUid, grid, xform.Coordinates);
        var path = (direction.ToWorldAngle() - _transform.GetWorldRotation(xform.ParentUid)).GetCardinalDir();
        var side = path.GetClockwise90Degrees();
        if (_random.Prob(0.5f))
            side = side.GetOpposite();

        foreach (var step in new[] { side, side.GetOpposite() })
        {
            var landing = tile + step.ToIntVec();
            if (!_map.TryGetTileRef(xform.ParentUid, grid, landing, out var tileRef) || tileRef.Tile.IsEmpty ||
                _turf.IsTileBlocked(xform.ParentUid, landing, CollisionGroup.Impassable, grid))
                continue;

            _transform.SetCoordinates(mob, _map.GridTileToLocal(xform.ParentUid, grid, landing));
            return;
        }
    }
}
