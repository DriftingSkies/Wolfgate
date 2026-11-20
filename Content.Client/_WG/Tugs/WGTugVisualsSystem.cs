using System.Numerics;
using Content.Shared._WG.Tugs;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Containers;
using Robust.Shared.Graphics.RSI;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WG.Tugs;

/// <summary>
/// Draws a tug over its rider when it faces up or down the screen, and under them side-on, like the ride-on ATV; and
/// draws its train where the tug seen on this screen puts it. The server places the cars behind the tug it knows,
/// which on the driver's screen is a round trip behind the predicted one, so at speed the cars would trail a gap.
/// Each frame the client lays the same trail from the tug it draws and offsets each car's sprite (and its crate's) to
/// where the server will put it.
/// </summary>
public sealed class WGTugVisualsSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    private EntityQuery<SpriteComponent> _sprites;
    private EntityQuery<TransformComponent> _xforms;
    private EntityQuery<WGTugTrolleyComponent> _trolleys;

    public override void Initialize()
    {
        base.Initialize();
        _sprites = GetEntityQuery<SpriteComponent>();
        _xforms = GetEntityQuery<TransformComponent>();
        _trolleys = GetEntityQuery<WGTugTrolleyComponent>();
        // The cars are offset from the lerped positions the renderer will use, so this runs after they are set.
        UpdatesAfter.Add(typeof(TransformSystem));

        SubscribeLocalEvent<WGTugTrolleyComponent, AfterAutoHandleStateEvent>(OnTrolleyState);
    }

    /// <summary>A trolley let go draws where it is again.</summary>
    private void OnTrolleyState(Entity<WGTugTrolleyComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (ent.Comp.Tug == null)
            OffsetCar(ent, Vector2.Zero);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var eyeRotation = _eye.CurrentEye.Rotation;
        var query = EntityQueryEnumerator<WGTugComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var tug, out var sprite, out var xform))
        {
            var direction = SpriteComponent.Layer.GetDirection(RsiDirectionType.Dir4, _transform.GetWorldRotation(xform) + eyeRotation);
            var depth = direction is RsiDirection.North or RsiDirection.South
                ? (int) DrawDepth.Doors
                : (int) DrawDepth.Objects;

            if (sprite.DrawDepth != depth)
                _sprite.SetDrawDepth((uid, sprite), depth);

            UpdateTrain((uid, tug), xform);
        }
    }

    private void UpdateTrain(Entity<WGTugComponent> tug, TransformComponent xform)
    {
        var count = tug.Comp.Trolleys.Count;
        var grid = xform.ParentUid;
        if (count == 0 || xform.GridUid != grid)
        {
            tug.Comp.Trail.Clear();
            tug.Comp.TrailGrid = null;
            tug.Comp.TrailCars = 0;
            return;
        }

        var pos = xform.LocalPosition;
        var trail = tug.Comp.Trail;
        if (tug.Comp.TrailGrid != grid || trail.Count == 0 || count > tug.Comp.TrailCars)
        {
            // Like the server on coupling, the trail starts through the cars where they stand.
            var cars = new List<Vector2>(count);
            foreach (var car in tug.Comp.Trolleys)
            {
                cars.Add(_xforms.TryComp(car, out var carXform) && carXform.ParentUid == grid ? carXform.LocalPosition : pos);
            }

            WGTugTrail.Rebuild(trail, pos, xform.LocalRotation.ToWorldVec(), cars, out tug.Comp.TailForward);
            tug.Comp.TrailGrid = grid;
        }

        tug.Comp.TrailCars = count;
        WGTugTrail.Advance(trail, pos, ref tug.Comp.TailForward);

        var fallback = -xform.LocalRotation.ToWorldVec();
        var gridRotation = _transform.GetWorldRotation(grid);
        for (var i = 0; i < count; i++)
        {
            var car = tug.Comp.Trolleys[i];
            if (!_xforms.TryComp(car, out var carXform) || carXform.ParentUid != grid)
                continue;

            var (point, _) = WGTugTrail.Sample(pos, trail, WGTugTrail.Distance(tug.Comp, i), fallback);
            OffsetCar(car, gridRotation.RotateVec(point - carXform.LocalPosition));
        }

        WGTugTrail.Trim(trail, pos, WGTugTrail.Distance(tug.Comp, count - 1) + WGTugTrail.Slack, ref tug.Comp.TailForward);
    }

    /// <summary>Shifts a car's sprite, and its crate's, by a world-space offset.</summary>
    private void OffsetCar(EntityUid car, Vector2 world)
    {
        OffsetSprite(car, world);
        if (_trolleys.TryComp(car, out var trolley) &&
            _containers.TryGetContainer(car, trolley.ContainerId, out var container))
        {
            foreach (var cargo in container.ContainedEntities)
            {
                OffsetSprite(cargo, world);
            }
        }
    }

    private void OffsetSprite(EntityUid uid, Vector2 world)
    {
        if (!_sprites.TryComp(uid, out var sprite))
            return;

        // A sprite's offset is in its own frame: rotated with the entity, or with the eye for one that never turns.
        var local = sprite.NoRotation
            ? _eye.CurrentEye.Rotation.RotateVec(world)
            : (-_transform.GetWorldRotation(uid)).RotateVec(world);

        if ((sprite.Offset - local).LengthSquared() > 0.000001f)
            _sprite.SetOffset((uid, sprite), local);
    }
}
