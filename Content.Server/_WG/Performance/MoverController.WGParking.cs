using System.Numerics;
using Content.Server._WG.Performance;
using Content.Shared._WG.Performance;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Physics.Controllers;

public sealed partial class MoverController
{
    [Dependency] private IConfigurationManager _wgCfg = default!;
    [Dependency] private EntityQuery<RelayInputMoverComponent> _wgRelaySourceQuery = default!;

    /// <summary>Ticks between rechecks of every parked mover's input.</summary>
    private const uint WGParkSweepTicks = 10;

    private bool _wgPark;
    private int _wgParkedBefore;
    private GameTick _wgLastSweep;
    private readonly List<EntityUid> _wgToPark = new();
    private readonly HashSet<EntityUid> _wgToWake = new();

    private void WGInitializeParking()
    {
        Subs.CVar(_wgCfg, WGPerformanceCVars.ParkIdleMovers, v => _wgPark = v, true);
        SubscribeLocalEvent<WGParkedMoverComponent, PhysicsWakeEvent>(OnParkedWake);
        SubscribeLocalEvent<WGParkedMoverComponent, MoveInputEvent>(OnParkedInput);
        SubscribeLocalEvent<WGParkedMoverComponent, PlayerAttachedEvent>(OnParkedAttached);
        SubscribeLocalEvent<WGParkedMoverComponent, SpriteMoveEvent>(OnParkedSteered);
        SubscribeLocalEvent<ActiveInputMoverComponent, ComponentStartup>(OnActiveStartup);
    }

    // Anything that puts a mover back on the list (unpausing, becoming a relay target) ends its parking.
    private void OnActiveStartup(Entity<ActiveInputMoverComponent> ent, ref ComponentStartup args) => RemComp<WGParkedMoverComponent>(ent);

    // Wakes are queued and applied before the next mover walk, never during it.
    private void OnParkedWake(Entity<WGParkedMoverComponent> ent, ref PhysicsWakeEvent args) => _wgToWake.Add(ent);

    private void OnParkedInput(Entity<WGParkedMoverComponent> ent, ref MoveInputEvent args) => _wgToWake.Add(ent);

    private void OnParkedAttached(Entity<WGParkedMoverComponent> ent, ref PlayerAttachedEvent args) => _wgToWake.Add(ent);

    // NPC steering writes its direction straight into the mover and raises this; a zero direction changes nothing.
    private void OnParkedSteered(Entity<WGParkedMoverComponent> ent, ref SpriteMoveEvent args)
    {
        if (MoverQuery.TryComp(ent, out var input) && !WGIdleInput(input))
            _wgToWake.Add(ent);
    }

    /// <summary>Whether a mover has no held buttons and no movement left from this tick.</summary>
    private bool WGIdleInput(InputMoverComponent mover)
    {
        return mover.HeldMoveButtons == MoveButtons.None
            && (Timing.CurTick > mover.LastInputTick
                || (mover.CurTickWalkMovement == Vector2.Zero && mover.CurTickSprintMovement == Vector2.Zero));
    }

    /// <summary>Puts woken movers back on the update list, or every parked mover if parking was turned off.</summary>
    private void WGWakeParked()
    {
        // Every parked mover is rechecked now and then, in case something set its input without an event.
        // Substeps call this several times a tick; sweep once.
        var sweep = Timing.CurTick.Value % WGParkSweepTicks == 0 && Timing.CurTick != _wgLastSweep;
        if (!_wgPark || sweep)
        {
            _wgLastSweep = Timing.CurTick;
            var parked = EntityQueryEnumerator<WGParkedMoverComponent, InputMoverComponent>();
            while (parked.MoveNext(out var uid, out _, out var input))
            {
                if (!_wgPark || !WGIdleInput(input) || _wgActorQuery.HasComp(uid))
                    _wgToWake.Add(uid);
            }
        }

        foreach (var uid in _wgToWake)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            RemComp<WGParkedMoverComponent>(uid);
            UpdateMoverStatus((uid, null, null));
        }

        _wgToWake.Clear();
        _wgParkedBefore = EntityManager.Count<WGParkedMoverComponent>();
    }

    /// <summary>Queues an idle mover to leave the update list after the walk. Relay sources stay, as their targets queue them.</summary>
    private void WGQueuePark(EntityUid uid)
    {
        if (_wgPark && !_wgRelaySourceQuery.HasComp(uid))
            _wgToPark.Add(uid);
    }

    /// <summary>Parks the queued movers and reports how many idle movers this step left alone.</summary>
    private void WGParkQueued()
    {
        foreach (var uid in _wgToPark)
        {
            RemComp<ActiveInputMoverComponent>(uid);
            EnsureComp<WGParkedMoverComponent>(uid);
        }

        _wgToPark.Clear();
        WGIdleSkippedGauge.Set(_wgIdleSkipped + _wgParkedBefore);
    }
}
