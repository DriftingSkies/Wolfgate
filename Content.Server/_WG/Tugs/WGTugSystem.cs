using System.Numerics;
using Content.Shared._WG.Tugs;
using Content.Shared._WG.Vehicles.Grid.Components;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Pulling.Events;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.Tugs;

/// <summary>Luggage tugs: the trolley train that follows the tug's path, and the crates strapped to the trolleys.</summary>
public sealed partial class WGTugSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private TurfSystem _turf = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WGTugComponent, MapInitEvent>(OnTugMapInit);
        SubscribeLocalEvent<WGTugComponent, ComponentShutdown>(OnTugShutdown);
        SubscribeLocalEvent<WGTugComponent, ExaminedEvent>(OnTugExamined);

        SubscribeLocalEvent<WGTugTrolleyComponent, ComponentInit>(OnTrolleyInit);
        SubscribeLocalEvent<WGTugTrolleyComponent, ComponentStartup>(OnTrolleyStartup);
        SubscribeLocalEvent<WGTugTrolleyComponent, ComponentShutdown>(OnTrolleyShutdown);
        SubscribeLocalEvent<WGTugTrolleyComponent, GetVerbsEvent<AlternativeVerb>>(OnTrolleyGetVerbs);
        SubscribeLocalEvent<WGTugTrolleyComponent, BeingPulledAttemptEvent>(OnTrolleyPullAttempt);
        SubscribeLocalEvent<WGTugTrolleyComponent, ExaminedEvent>(OnTrolleyExamined);
        SubscribeLocalEvent<WGTugTrolleyComponent, WGTrolleyLoadDoAfterEvent>(OnLoadDoAfter);
        SubscribeLocalEvent<WGTugTrolleyComponent, WGTrolleyUnloadDoAfterEvent>(OnUnloadDoAfter);
    }

    private void OnTugMapInit(Entity<WGTugComponent> ent, ref MapInitEvent args)
    {
        // A tug loaded from a save already knows its bases; the mover may hold a towing penalty.
        if (ent.Comp.BaseMaxSpeed > 0f || !TryComp(ent, out GridVehicleMoverComponent? mover))
            return;

        ent.Comp.BaseMaxSpeed = mover.MaxSpeed;
        ent.Comp.BaseMaxReverseSpeed = mover.MaxReverseSpeed;
        ent.Comp.BaseAcceleration = mover.Acceleration;
    }

    private void OnTugShutdown(Entity<WGTugComponent> ent, ref ComponentShutdown args)
    {
        UncoupleFrom(ent, 0);
    }

    private void OnTugExamined(Entity<WGTugComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-tug-examine-trolleys",
            ("count", ent.Comp.Trolleys.Count),
            ("max", ent.Comp.MaxTrolleys)));
    }

    /// <summary>A trolley saved mid-tow loads without its tug; it is put back to free physics.</summary>
    private void OnTrolleyStartup(Entity<WGTugTrolleyComponent> ent, ref ComponentStartup args)
    {
        if (!ent.Comp.Towed || ent.Comp.Tug != null)
            return;

        SetTowedPhysics(ent, false);
    }

    private void OnTrolleyShutdown(Entity<WGTugTrolleyComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Tug is not { } tug || !TryComp(tug, out WGTugComponent? tugComp))
            return;

        var index = tugComp.Trolleys.IndexOf(ent.Owner);
        if (index >= 0)
            UncoupleFrom((tug, tugComp), index);
    }

    private void OnTrolleyPullAttempt(Entity<WGTugTrolleyComponent> ent, ref BeingPulledAttemptEvent args)
    {
        if (ent.Comp.Tug != null)
            args.Cancel();
    }

    private void OnTrolleyExamined(Entity<WGTugTrolleyComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Tug != null)
            args.PushMarkup(Loc.GetString("wg-trolley-examine-coupled"));

        if (ent.Comp.Cargo?.ContainedEntity is { } cargo)
            args.PushMarkup(Loc.GetString("wg-trolley-examine-cargo", ("cargo", cargo)));
    }

    private void OnTrolleyGetVerbs(Entity<WGTugTrolleyComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var user = args.User;
        if (ent.Comp.Tug is { } tug)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wg-trolley-verb-uncouple"),
                Priority = 1,
                Act = () => TryUncouple(ent, user),
            });
        }
        else if (FindCouplingTug(ent) is { } candidate)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wg-trolley-verb-couple"),
                Priority = 1,
                Act = () => TryCouple(candidate, ent, user),
            });
        }

        AddCargoVerbs(ent, user, args);
    }

    /// <summary>Hitches a trolley behind the last car of a stationary tug's train.</summary>
    public bool TryCouple(Entity<WGTugComponent> tug, Entity<WGTugTrolleyComponent> trolley, EntityUid? user = null)
    {
        if (trolley.Comp.Tug != null)
            return false;

        if (tug.Comp.Trolleys.Count >= tug.Comp.MaxTrolleys)
        {
            Popup(trolley, user, "wg-trolley-couple-full");
            return false;
        }

        if (IsMoving(tug))
        {
            Popup(trolley, user, "wg-trolley-moving");
            return false;
        }

        var tugXform = Transform(tug);
        if (Transform(trolley).GridUid is not { } grid || tugXform.ParentUid != grid || !InCoupleRange(tug, trolley))
        {
            Popup(trolley, user, "wg-trolley-couple-far");
            return false;
        }

        if (TryComp(trolley, out PullableComponent? pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(trolley, pullable, user);

        tug.Comp.Trolleys.Add(trolley);
        trolley.Comp.Tug = tug;
        Dirty(tug);
        Dirty(trolley);
        SetTowedPhysics(trolley, true);
        RebuildTrail(tug, grid);
        RefreshSpeed(tug);
        Popup(trolley, user, "wg-trolley-coupled");
        return true;
    }

    /// <summary>Unhitches a trolley, and every car behind it, from its tug.</summary>
    public bool TryUncouple(Entity<WGTugTrolleyComponent> trolley, EntityUid? user = null)
    {
        if (trolley.Comp.Tug is not { } tug || !TryComp(tug, out WGTugComponent? tugComp))
            return false;

        if (IsMoving(tug))
        {
            Popup(trolley, user, "wg-trolley-moving");
            return false;
        }

        var index = tugComp.Trolleys.IndexOf(trolley);
        if (index < 0)
            return false;

        UncoupleFrom((tug, tugComp), index);
        Popup(trolley, user, "wg-trolley-uncoupled");
        return true;
    }

    /// <summary>Drops every trolley from the given position in the train back.</summary>
    private void UncoupleFrom(Entity<WGTugComponent> tug, int index)
    {
        for (var i = tug.Comp.Trolleys.Count - 1; i >= index; i--)
        {
            var car = tug.Comp.Trolleys[i];
            tug.Comp.Trolleys.RemoveAt(i);

            if (!TryComp(car, out WGTugTrolleyComponent? trolley))
                continue;

            trolley.Tug = null;
            if (TerminatingOrDeleted(car))
                continue;

            Dirty(car, trolley);
            SetTowedPhysics((car, trolley), false);
            SettleOnFreeTile(car);
        }

        if (tug.Comp.Trolleys.Count == 0)
        {
            tug.Comp.Trail.Clear();
            tug.Comp.TrailGrid = null;
        }

        if (TerminatingOrDeleted(tug))
            return;

        Dirty(tug);
        RefreshSpeed(tug);
    }

    /// <summary>A released trolley that stands in a wall is moved to the nearest free tile before it collides again.</summary>
    private void SettleOnFreeTile(EntityUid trolley)
    {
        var xform = Transform(trolley);
        if (xform.GridUid is not { } grid || !TryComp(grid, out MapGridComponent? gridComp))
            return;

        var tile = _maps.TileIndicesFor(grid, gridComp, xform.Coordinates);
        if (!_turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable, gridComp))
            return;

        for (var ring = 1; ring <= 2; ring++)
        {
            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                        continue;

                    var candidate = tile + new Vector2i(dx, dy);
                    if (_turf.IsTileBlocked(grid, candidate, CollisionGroup.Impassable, gridComp) ||
                        _maps.GetTileRef(grid, gridComp, candidate).Tile.IsEmpty)
                    {
                        continue;
                    }

                    _transform.SetCoordinates(trolley, _maps.GridTileToLocal(grid, gridComp, candidate));
                    return;
                }
            }
        }
    }

    /// <summary>A towed trolley is placed by the train, so it neither collides nor drifts.</summary>
    private void SetTowedPhysics(Entity<WGTugTrolleyComponent> trolley, bool towed)
    {
        trolley.Comp.Towed = towed;
        if (!TryComp(trolley, out PhysicsComponent? body))
            return;

        _physics.SetLinearVelocity(trolley, Vector2.Zero, body: body);
        _physics.SetAngularVelocity(trolley, 0f, body: body);
        _physics.SetBodyType(trolley, towed ? BodyType.Kinematic : BodyType.Dynamic, body: body);
        _physics.SetCanCollide(trolley, !towed, body: body);
    }

    /// <summary>Each trolley costs the tug some top speed and acceleration.</summary>
    private void RefreshSpeed(Entity<WGTugComponent> tug)
    {
        if (!TryComp(tug, out GridVehicleMoverComponent? mover) || tug.Comp.BaseMaxSpeed <= 0f)
            return;

        var count = tug.Comp.Trolleys.Count;
        var speed = MathF.Max(0.3f, 1f - count * tug.Comp.SpeedPenaltyPerTrolley);
        var accel = MathF.Max(0.3f, 1f - count * tug.Comp.AccelerationPenaltyPerTrolley);
        mover.MaxSpeed = tug.Comp.BaseMaxSpeed * speed;
        mover.MaxReverseSpeed = tug.Comp.BaseMaxReverseSpeed * speed;
        mover.Acceleration = tug.Comp.BaseAcceleration * accel;
        Dirty(tug, mover);
    }

    private bool IsMoving(EntityUid tug)
    {
        return TryComp(tug, out GridVehicleMoverComponent? mover) &&
               (mover.IsMoving || MathF.Abs(mover.CurrentSpeed) > 0.05f);
    }

    /// <summary>The tug whose train ends close enough to this trolley to take it, if any.</summary>
    private Entity<WGTugComponent>? FindCouplingTug(Entity<WGTugTrolleyComponent> trolley)
    {
        var query = EntityQueryEnumerator<WGTugComponent>();
        while (query.MoveNext(out var uid, out var tug))
        {
            if (tug.Trolleys.Count < tug.MaxTrolleys && InCoupleRange((uid, tug), trolley))
                return (uid, tug);
        }

        return null;
    }

    /// <summary>The trolley stands behind the back of the train, on its grid, within range and not far off its line.</summary>
    private bool InCoupleRange(Entity<WGTugComponent> tug, EntityUid trolley)
    {
        var back = tug.Comp.Trolleys.Count > 0 ? tug.Comp.Trolleys[^1] : tug.Owner;
        var backXform = Transform(back);
        var trolleyXform = Transform(trolley);
        if (!backXform.ParentUid.IsValid() || backXform.ParentUid != trolleyXform.ParentUid)
            return false;

        var backward = -backXform.LocalRotation.ToWorldVec();
        var offset = trolleyXform.LocalPosition - backXform.LocalPosition;
        var along = Vector2.Dot(offset, backward);
        var aside = MathF.Abs(offset.X * backward.Y - offset.Y * backward.X);

        return along >= 0.5f && along <= tug.Comp.CoupleRange && aside <= 0.5f;
    }

    /// <summary>Lays the trail through the cars where they stand, so a fresh coupling settles in place.</summary>
    private void RebuildTrail(Entity<WGTugComponent> tug, EntityUid grid)
    {
        tug.Comp.TrailGrid = grid;
        var cars = new List<Vector2>(tug.Comp.Trolleys.Count);
        foreach (var car in tug.Comp.Trolleys)
        {
            cars.Add(Transform(car).LocalPosition);
        }

        var xform = Transform(tug);
        WGTugTrail.Rebuild(tug.Comp.Trail, xform.LocalPosition, xform.LocalRotation.ToWorldVec(), cars, out tug.Comp.TailForward);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WGTugComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var tug, out var xform))
        {
            if (tug.Trolleys.Count == 0)
                continue;

            UpdateTrain((uid, tug), xform);
        }
    }

    private void UpdateTrain(Entity<WGTugComponent> tug, TransformComponent xform)
    {
        // A tug that leaves its grid (a fall, a jump, another ship) leaves its train behind.
        if (tug.Comp.TrailGrid is not { } grid || xform.ParentUid != grid)
        {
            UncoupleFrom(tug, 0);
            return;
        }

        var trail = tug.Comp.Trail;
        var pos = xform.LocalPosition;
        var reversing = TryComp(tug, out GridVehicleMoverComponent? mover) && mover.CurrentSpeed < 0f;

        WGTugTrail.Advance(trail, pos, ref tug.Comp.TailForward);

        var fallback = -xform.LocalRotation.ToWorldVec();

        // The train cannot back through a wall: a car about to enter one stops the tug.
        if (reversing && mover != null && TryComp(grid, out MapGridComponent? gridComp))
        {
            var lastDistance = WGTugTrail.Distance(tug.Comp, tug.Comp.Trolleys.Count - 1);
            var (lastPoint, _) = WGTugTrail.Sample(pos, trail, lastDistance, fallback);
            var tile = _maps.TileIndicesFor(grid, gridComp, new EntityCoordinates(grid, lastPoint));
            if (_turf.IsTileBlocked(grid, tile, CollisionGroup.Impassable, gridComp))
            {
                // Back where the train last stood free, or the tug would creep into its own cars tick by tick.
                var last = tug.Comp.LastFreePosition;
                mover.CurrentSpeed = 0f;
                mover.TravelVelocity = Vector2.Zero;
                mover.Position = last;
                mover.TargetPosition = last;
                Dirty(tug, mover);
                _transform.SetLocalPosition(tug, last, xform);
                return;
            }
        }

        tug.Comp.LastFreePosition = pos;
        var needed = WGTugTrail.Distance(tug.Comp, tug.Comp.Trolleys.Count - 1);

        for (var i = 0; i < tug.Comp.Trolleys.Count; i++)
        {
            var car = tug.Comp.Trolleys[i];
            if (TerminatingOrDeleted(car))
            {
                UncoupleFrom(tug, i);
                return;
            }

            var (point, forward) = WGTugTrail.Sample(pos, trail, WGTugTrail.Distance(tug.Comp, i), fallback);
            PlaceCar(car, grid, point, forward.ToWorldAngle());
        }

        WGTugTrail.Trim(trail, pos, needed + WGTugTrail.Slack, ref tug.Comp.TailForward);
    }

    private void PlaceCar(EntityUid car, EntityUid grid, Vector2 point, Angle rotation)
    {
        var xform = Transform(car);
        if (xform.ParentUid != grid)
        {
            _transform.SetCoordinates(car, xform, new EntityCoordinates(grid, point), rotation);
            return;
        }

        if ((xform.LocalPosition - point).LengthSquared() < 0.00001f &&
            Math.Abs(Angle.ShortestDistance(xform.LocalRotation, rotation).Degrees) < 0.5)
        {
            return;
        }

        _transform.SetLocalPositionRotation(car, point, rotation, xform);
    }

    private void Popup(EntityUid target, EntityUid? user, string message)
    {
        if (user is { } u)
            _popup.PopupEntity(Loc.GetString(message), target, u);
    }
}
