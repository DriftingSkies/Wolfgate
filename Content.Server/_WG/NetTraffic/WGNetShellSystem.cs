using System.Numerics;
using Content.Server._Mono.Projectiles.TargetGuided;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Shared._Goobstation.Wizard.Projectiles;
using Content.Shared._Mono.SpaceArtillery;
using Content.Shared._WG.NetTraffic;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Enums;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._WG.NetTraffic;

/// <summary>
/// Keeps unguided ship shells off the network. Each is put on a layer no eye sees; the players near its flight get one
/// message when it is fired, another if its course changes and one when it ends, and their clients draw it.
/// </summary>
public sealed class WGNetShellSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private WGViewPointSystem _viewPoints = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VisibilitySystem _visibility = default!;

    /// <summary>A course change smaller than this, in metres a second, isn't sent.</summary>
    private const float VelocityTolerance = 0.5f;

    /// <summary>The longest flight considered when deciding who is told about a shell.</summary>
    private const float MaxFlight = 20000f;

    /// <summary>How often, in ticks, shells in flight look for players who have come into view of them.</summary>
    private const int RecheckTicks = 10;

    private bool _enabled;
    private float _margin;
    private int _nextId;
    private readonly Dictionary<ICommonSession, WGShellBatchEvent> _batches = new();
    private readonly List<(ICommonSession Session, MapCoordinates At)> _viewers = new();
    private readonly HashSet<ICommonSession> _told = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_config, NetTrafficCVars.Shells, value => _enabled = value, true);
        Subs.CVar(_config, NetTrafficCVars.ShellMargin, value => _margin = value, true);
        SubscribeLocalEvent<ShipWeaponProjectileComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WGNetShellComponent, ComponentShutdown>(OnShutdown);
    }

    /// <summary>Hides an unguided shell from every client the moment it exists, before it is ever sent.</summary>
    private void OnMapInit(Entity<ShipWeaponProjectileComponent> ent, ref MapInitEvent args)
    {
        if (!_enabled
            || HasComp<TargetGuidedComponent>(ent)
            || HasComp<TargetSeekingComponent>(ent)
            || HasComp<HomingProjectileComponent>(ent))
            return;

        var shell = AddComp<WGNetShellComponent>(ent);
        shell.Id = ++_nextId;
        _visibility.AddLayer(ent.Owner, WGNetTraffic.NeverSentLayer);
    }

    private void OnShutdown(Entity<WGNetShellComponent> ent, ref ComponentShutdown args)
    {
        if (!ent.Comp.Announced || ent.Comp.Viewers.Count == 0)
            return;

        var ended = new WGShellEnded(ent.Comp.Id, _transform.GetWorldPosition(ent));
        foreach (var session in ent.Comp.Viewers)
        {
            Batch(session).Ended.Add(ended);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _viewPoints.Collect(_viewers);
        var recheck = _timing.CurTick.Value % RecheckTicks == 0;

        var query = EntityQueryEnumerator<WGNetShellComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var shell, out var xform))
        {
            var velocity = _physics.GetMapLinearVelocity(uid);
            if (!shell.Announced)
            {
                // A shell spawns before its gun gives it a speed; it is announced once it moves.
                if (velocity.LengthSquared() > 0f)
                    Announce((uid, shell, xform), velocity);
                continue;
            }

            // Players who came into view of the rest of its flight since, such as through a camera, are told now.
            if (recheck)
                Tell((uid, shell, xform), velocity);

            if ((velocity - shell.Velocity).LengthSquared() < VelocityTolerance * VelocityTolerance)
                continue;

            shell.Velocity = velocity;
            var moved = new WGShellMoved(shell.Id, _transform.GetWorldPosition(xform), velocity,
                (float) _transform.GetWorldRotation(xform).Theta, _timing.CurTick.Value);
            foreach (var session in shell.Viewers)
            {
                Batch(session).Moved.Add(moved);
            }
        }

        foreach (var (session, batch) in _batches)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(batch, session);
        }

        _batches.Clear();
    }

    private void Announce(Entity<WGNetShellComponent, TransformComponent> ent, Vector2 velocity)
    {
        ent.Comp1.Announced = true;
        ent.Comp1.Velocity = velocity;
        Tell(ent, velocity);
    }

    /// <summary>
    /// Sends the shell as fired, from where it is now, to every player within the margin of the rest of its flight who
    /// hasn't been told yet.
    /// </summary>
    private void Tell(Entity<WGNetShellComponent, TransformComponent> ent, Vector2 velocity)
    {
        var (uid, shell, xform) = ent;
        if (xform.MapUid is not { } map || MetaData(uid).EntityPrototype is not { } proto)
            return;

        var lifetime = TryComp<TimedDespawnComponent>(uid, out var despawn) ? despawn.Lifetime : 5f;
        var position = _transform.GetWorldPosition(xform);
        var speed = velocity.Length();
        var flight = speed > 0f ? velocity / speed * MathF.Min(MaxFlight, speed * lifetime) : Vector2.Zero;
        _told.Clear();
        _viewPoints.NearSegment(_viewers, xform.MapID, position, position + flight, _margin, _told);
        if (_told.Count == 0)
            return;

        WGShellFired? fired = null;
        foreach (var session in _told)
        {
            if (shell.Viewers.Contains(session))
                continue;

            fired ??= new WGShellFired(shell.Id, proto.ID, GetNetEntity(map), position, velocity,
                (float) _transform.GetWorldRotation(xform).Theta, _timing.CurTick.Value, lifetime);
            shell.Viewers.Add(session);
            Batch(session).Fired.Add(fired.Value);
        }
    }

    private WGShellBatchEvent Batch(ICommonSession session)
    {
        if (!_batches.TryGetValue(session, out var batch))
            _batches[session] = batch = new WGShellBatchEvent();
        return batch;
    }
}
