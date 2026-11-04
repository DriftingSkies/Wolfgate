using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Ward;
using Content.Shared._WF.ShipShields;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Ward;

/// <summary>
/// The Ward gift. The stone is a ship shield generator that costs no power: it feeds the stock <c>ShipShieldEmitter</c>, so the
/// field, what it blocks and how it breaks are the ship shield's own. The Ward dial and the stone's resonance set the
/// emitter's capacity and mending; a rooted stone's field can't be dented. Rooted, the field also holds other ships out: any
/// ship that isn't the stone's own or docked to it is pushed back to the edge, so nobody can fly in and fire from inside.
/// People on foot, in jetpacks or in suits aren't ships and cross as they like.
/// </summary>
public sealed partial class WFWardSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;
    [Dependency] private WFMonolithSystem _monolith = default!;

    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(0.5);
    private static readonly Color FieldColor = Color.FromHex("#EC3232");

    /// <summary>Speed a turned-back ship is given away from the wall, as a share of the speed it hit it at.</summary>
    private const float Bounce = 0.25f;

    private readonly HashSet<EntityUid> _docked = new();
    private List<Entity<MapGridComponent>> _grids = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFWardComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WFWardComponent> ent, ref MapInitEvent args)
    {
        var receiver = EnsureComp<ApcPowerReceiverComponent>(ent);
        receiver.NeedsPower = false;
        receiver.Load = 0f;
        receiver.PowerDisabled = true;

        var emitter = EnsureComp<ShipShieldEmitterComponent>(ent);
        emitter.ShieldColor = FieldColor;
        emitter.BaseDraw = 0f;
        emitter.MaxDraw = 1f;
        emitter.PowerModifier = 0f;
        emitter.UnpoweredBonus = 1f;
        emitter.DamageOverloadTimePunishment = WFMonolithMath.WardOverloadSeconds;
        emitter.DamageLimit = 1f;
        emitter.HealPerSecond = 0f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFWardComponent, WFMonolithComponent, ShipShieldEmitterComponent, ApcPowerReceiverComponent>();
        while (query.MoveNext(out var uid, out var ward, out var monolith, out var emitter, out var receiver))
        {
            if (now >= ward.Next)
            {
                ward.Next = now + TickRate;
                Tick((uid, monolith), ward, emitter, receiver);
            }

            if (ward.Walled)
                HoldOut(uid, ward);
        }
    }

    /// <summary>Sets the emitter to what the dial and the stage give, and works out whether the wall is up.</summary>
    private void Tick(Entity<WFMonolithComponent> scion, WFWardComponent ward, ShipShieldEmitterComponent emitter, ApcPowerReceiverComponent receiver)
    {
        var monolith = scion.Comp;
        var barred = _monolith.IsBarred(scion);
        var dial = _monolith.GetDial(scion, WFGift.Ward);
        var capacity = barred ? 0f : WFMonolithMath.WardStrength(monolith.Stage, dial, monolith.Resonance);

        if (capacity <= 0f)
        {
            // With the dial off the emitter has no power, which drops the field.
            receiver.PowerDisabled = true;
            ward.Capacity = 0f;
            ward.Walled = false;
            return;
        }

        var rooted = monolith.Stage == WFMonolithStage.Crescendo;
        if (ward.Capacity <= 0f)
        {
            // Switched on fresh, not still sick from the last time.
            emitter.Damage = 0f;
            emitter.Recharging = false;
            emitter.OverloadAccumulator = 0f;
        }
        else if (!rooted && Math.Abs(ward.Capacity - capacity) > 0.5f)
        {
            emitter.Damage *= capacity / ward.Capacity;
        }

        ward.Capacity = capacity;
        receiver.PowerDisabled = false;
        if (emitter.Shield is { } field)
            MarkField(field, monolith.Stage, rooted);

        emitter.DamageLimit = capacity;
        emitter.HealPerSecond = WFMonolithMath.WardMendRate(monolith.Stage, dial, monolith.Resonance);

        // A rooted stone can't be broken, so nothing the ships throw leaves a mark.
        if (rooted)
        {
            emitter.Damage = 0f;
            emitter.Recharging = false;
            emitter.OverloadAccumulator = 0f;
        }

        ward.Walled = rooted && emitter.Shield is { } shield && SetOutline(ward, shield) && Transform(scion).GridUid is { } grid
            && SetFriendly(ward, grid);
    }

    /// <summary>Tells clients this shield is the stone's field and how far gone the stone is, so they draw it their own way.</summary>
    private void MarkField(EntityUid shield, WFMonolithStage stage, bool rooted)
    {
        var field = EnsureComp<WFWardFieldComponent>(shield);
        if (field.Stage == stage && field.Rooted == rooted)
            return;

        field.Stage = stage;
        field.Rooted = rooted;
        Dirty(shield, field);
    }

    /// <summary>Takes the wall's shape from the field's outline, which is an oval round the ship.</summary>
    private bool SetOutline(WFWardComponent ward, EntityUid shield)
    {
        if (!TryComp<WFShipShieldVisualsComponent>(shield, out var visuals) || visuals.Contours.Length == 0)
            return false;

        if (ReferenceEquals(ward.Contours, visuals.Contours))
            return true;

        var points = visuals.Contours.SelectMany(contour => contour).ToArray();
        var min = points.Aggregate(Vector2.Min);
        var max = points.Aggregate(Vector2.Max);
        ward.Centre = (min + max) / 2f;
        ward.Radii = (max - min) / 2f;
        ward.Contours = visuals.Contours;
        return true;
    }

    private bool SetFriendly(WFWardComponent ward, EntityUid grid)
    {
        _docked.Clear();
        _shuttle.GetAllDockedShuttles(grid, _docked);
        _docked.Add(grid);
        if (!_docked.SetEquals(ward.Friendly))
            ward.Friendly = new HashSet<EntityUid>(_docked);

        return true;
    }

    /// <summary>
    /// Pushes every ship that isn't friendly out of the oval. A ship is tested at its corners and edge midpoints; the
    /// deepest one is moved back to the edge, and the speed it was carrying into the wall is taken off it.
    /// </summary>
    private void HoldOut(EntityUid scion, WFWardComponent ward)
    {
        if (Transform(scion).GridUid is not { } home || TerminatingOrDeleted(home))
            return;

        var homeXform = Transform(home);
        if (homeXform.MapUid == null)
            return;

        var (_, _, toWorld, toLocal) = _transform.GetWorldPositionRotationMatrixWithInv(homeXform);
        var world = toWorld.TransformBox(Box2.CenteredAround(ward.Centre, ward.Radii * 2f));

        // Wide enough that a big ship straddling the edge is found by its box.
        _grids.Clear();
        _maps.FindGridsIntersecting(homeXform.MapID, world.Enlarged(60f), ref _grids);
        foreach (var other in _grids)
        {
            if (ward.Friendly.Contains(other.Owner) || !TryComp<PhysicsComponent>(other, out var body) || body.BodyType != BodyType.Dynamic)
                continue;

            var otherXform = Transform(other);
            var otherToLocal = _transform.GetWorldMatrix(otherXform) * toLocal;
            var local = other.Comp.LocalAABB;
            var worst = 1f;
            var deepest = Vector2.Zero;
            foreach (var sample in Samples(local))
            {
                var inHome = Vector2.Transform(sample, otherToLocal);
                var offset = inHome - ward.Centre;
                var depth = MathF.Sqrt(offset.X * offset.X / (ward.Radii.X * ward.Radii.X) + offset.Y * offset.Y / (ward.Radii.Y * ward.Radii.Y));
                if (depth < worst)
                {
                    worst = depth;
                    deepest = inHome;
                }
            }

            if (worst >= 1f)
                continue;

            // Out along the radius to the edge, and the speed into the wall comes off.
            var from = deepest - ward.Centre;
            var edge = ward.Centre + from / Math.Max(worst, 0.001f);
            if (worst <= 0.001f)
                edge = ward.Centre + new Vector2(ward.Radii.X, 0f);

            var normal = Vector2.Normalize(new Vector2(from.X / (ward.Radii.X * ward.Radii.X), from.Y / (ward.Radii.Y * ward.Radii.Y)));
            if (!float.IsFinite(normal.X) || !float.IsFinite(normal.Y))
                normal = Vector2.UnitX;

            var shift = Vector2.TransformNormal(edge - deepest, toWorld);
            _transform.SetWorldPosition(other, _transform.GetWorldPosition(otherXform) + shift);

            var outward = Vector2.TransformNormal(normal, toWorld);
            outward = Vector2.Normalize(outward);
            var homeVelocity = CompOrNull<PhysicsComponent>(home)?.LinearVelocity ?? Vector2.Zero;
            var relative = body.LinearVelocity - homeVelocity;
            var into = Vector2.Dot(relative, outward);
            if (into < 0f)
                _physics.SetLinearVelocity(other, relative - (1f + Bounce) * into * outward + homeVelocity, body: body);

            ward.Turned++;
        }
    }

    /// <summary>The corners and the edge midpoints of a ship's box, in its own frame.</summary>
    private static IEnumerable<Vector2> Samples(Box2 box)
    {
        var halfX = (box.Left + box.Right) / 2f;
        var halfY = (box.Bottom + box.Top) / 2f;
        yield return box.BottomLeft;
        yield return new Vector2(halfX, box.Bottom);
        yield return box.BottomRight;
        yield return new Vector2(box.Right, halfY);
        yield return box.TopRight;
        yield return new Vector2(halfX, box.Top);
        yield return box.TopLeft;
        yield return new Vector2(box.Left, halfY);
    }

    /// <summary>True while a stone's wall is up and turning ships back. Tests read it.</summary>
    public bool IsWalled(EntityUid scion) => TryComp<WFWardComponent>(scion, out var ward) && ward.Walled;
}
