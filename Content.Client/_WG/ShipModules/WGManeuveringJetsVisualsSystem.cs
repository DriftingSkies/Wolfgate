using System.Numerics;
using Content.Client.Shuttles;
using Content.Shared._WG.ShipModules;
using Content.Shared.Shuttles.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WG.ShipModules;

/// <summary>
/// Plays a jets burn on the ship's thrusters: those pushing along it show their plume for the burn and puff smoke
/// rings out along their exhaust, bigger for stronger thrusters.
/// </summary>
public sealed class WGManeuveringJetsVisualsSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const int RingsPerThruster = 3;
    private const float RingInterval = 0.13f;
    private const int MaxRings = 256;

    /// <summary>Distance from a standard thruster's centre to where its rings appear, in tiles.</summary>
    private const float RingStart = 0.85f;

    /// <summary>Exhaust speed a ring leaves with, in tiles per second, before drag slows it.</summary>
    private const float RingSpeed = 7f;

    /// <summary>Smoke rings in flight, drawn by <see cref="WGSmokeRingOverlay"/>.</summary>
    public readonly List<WGSmokeRing> Rings = new();

    private readonly List<(EntityUid Thruster, WGJetsPuff Puff, TimeSpan At)> _pending = new();
    private readonly Dictionary<EntityUid, TimeSpan> _flaring = new();
    private readonly List<EntityUid> _settled = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGManeuveringJetsPuffEvent>(OnPuff);
        _overlays.AddOverlay(new WGSmokeRingOverlay(this, _sprite));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay<WGSmokeRingOverlay>();
    }

    private void OnPuff(WGManeuveringJetsPuffEvent ev)
    {
        var now = _timing.RealTime;
        var flareUntil = now + TimeSpan.FromSeconds(ev.BurnTime + 0.1f);
        foreach (var puff in ev.Puffs)
        {
            if (!TryGetEntity(puff.Thruster, out var thruster))
                continue;

            _flaring[thruster.Value] = flareUntil;
            for (var i = 0; i < RingsPerThruster; i++)
            {
                _pending.Add((thruster.Value, puff, now + TimeSpan.FromSeconds(i * RingInterval)));
            }
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.RealTime;

        foreach (var (thruster, until) in _flaring)
        {
            var flaring = now < until && !Deleted(thruster);
            SetPlume(thruster, flaring || IsThrusting(thruster));
            if (!flaring)
                _settled.Add(thruster);
        }

        foreach (var thruster in _settled)
        {
            _flaring.Remove(thruster);
        }

        _settled.Clear();

        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var (thruster, puff, at) = _pending[i];
            if (now < at)
                continue;

            _pending.RemoveAt(i);
            Emit(thruster, puff);
        }

        for (var i = Rings.Count - 1; i >= 0; i--)
        {
            var ring = Rings[i];
            ring.Age += frameTime;
            if (ring.Age >= WGSmokeRing.Life)
                Rings.RemoveAt(i);
        }
    }

    /// <summary>Puffs one ring from a thruster's nozzle along its exhaust.</summary>
    private void Emit(EntityUid thruster, WGJetsPuff puff)
    {
        if (Deleted(thruster) || !TryComp<SpriteComponent>(thruster, out var sprite))
            return;

        var xform = Transform(thruster);
        if (xform.MapID == MapId.Nullspace)
            return;

        if (Rings.Count >= MaxRings)
            Rings.RemoveAt(0);

        // A thruster at rotation 0 exhausts north; its sprite offset turns with it.
        var (position, rotation) = _transform.GetWorldPositionRotation(xform);
        var exhaust = rotation.RotateVec(Vector2.UnitY);
        var centre = position + rotation.RotateVec(sprite.Offset);
        var strength = Math.Clamp(puff.Strength, 0f, 1f);
        Rings.Add(new WGSmokeRing
        {
            Map = xform.MapID,
            Origin = centre + exhaust * (RingStart * puff.Size + _random.NextFloat(-0.08f, 0.08f)),
            Drift = _physics.GetMapLinearVelocity(thruster),
            Exhaust = exhaust,
            Speed = RingSpeed * (0.5f + 0.5f * strength) * (0.8f + 0.2f * puff.Size) * _random.NextFloat(0.9f, 1.1f),
            Size = puff.Size * (0.85f + 0.15f * strength) * _random.NextFloat(0.92f, 1.08f),
            Opacity = 0.55f + 0.45f * strength,
        });
    }

    /// <summary>Whether the thruster's own state shows its plume.</summary>
    private bool IsThrusting(EntityUid thruster)
    {
        return _appearance.TryGetData<bool>(thruster, ThrusterVisualState.State, out var on) && on
               && _appearance.TryGetData<bool>(thruster, ThrusterVisualState.Thrusting, out var thrusting) && thrusting;
    }

    private void SetPlume(EntityUid thruster, bool visible)
    {
        if (!TryComp<SpriteComponent>(thruster, out var sprite))
            return;

        Entity<SpriteComponent?> ent = (thruster, sprite);
        if (_sprite.LayerMapTryGet(ent, ThrusterVisualLayers.Thrusting, out var layer, false))
            _sprite.LayerSetVisible(ent, layer, visible);
        if (_sprite.LayerMapTryGet(ent, ThrusterVisualLayers.ThrustingUnshaded, out layer, false))
            _sprite.LayerSetVisible(ent, layer, visible);
    }
}

/// <summary>A smoke ring in flight: it carries its thruster's velocity, and its exhaust speed bleeds off.</summary>
public sealed class WGSmokeRing
{
    /// <summary>Seconds a ring lasts.</summary>
    public const float Life = 1.4f;

    /// <summary>How fast the exhaust speed bleeds off, per second.</summary>
    private const float ExhaustDrag = 2.5f;

    /// <summary>How fast the thruster's velocity bleeds off, so rings drift behind a moving ship.</summary>
    private const float DriftDrag = 0.8f;

    /// <summary>Map the ring was puffed on.</summary>
    public MapId Map;

    /// <summary>Where the thruster puffed it, in map space.</summary>
    public Vector2 Origin;

    /// <summary>The thruster's map velocity when it puffed.</summary>
    public Vector2 Drift;

    /// <summary>Unit direction the ring is blown out along.</summary>
    public Vector2 Exhaust;

    /// <summary>Starting exhaust speed, in metres per second.</summary>
    public float Speed;

    /// <summary>Width in metres when puffed; it spreads with age.</summary>
    public float Size;

    /// <summary>Starting opacity, from 0 to 1.</summary>
    public float Opacity;

    /// <summary>Seconds since it was puffed.</summary>
    public float Age;

    /// <summary>Where the ring is now, in map space.</summary>
    public Vector2 Position => Origin
                               + Drift * ((1f - MathF.Exp(-DriftDrag * Age)) / DriftDrag)
                               + Exhaust * (Speed * (1f - MathF.Exp(-ExhaustDrag * Age)) / ExhaustDrag);
}
