using System.Numerics;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.TractorBeam;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.TractorBeam;

public sealed partial class TractorBeamSystem
{
    private TimeSpan _nextAutoCatch;

    private void OnAutoCatch(EntityUid uid, TractorBeamConsoleComponent console, TractorBeamAutoCatchMessage args)
    {
        if (!_ui.IsUiOpen(uid, TractorBeamUiKey.Key, args.Actor) || !ConsoleReady(uid, out var source) ||
            !TryGetEntity(args.Emitter, out var emitterUid) || emitterUid is not { } emitter ||
            !TryComp<TractorBeamEmitterComponent>(emitter, out var beam) ||
            Transform(emitter).GridUid != source || !Transform(emitter).Anchored)
            return;

        beam.AutoCatch = args.Enabled;
        beam.AutoCatchConsole = args.Enabled ? uid : null;
        Dirty(emitter, beam);
        UpdateUi(uid, console);
    }

    private void UpdateAutoCatch()
    {
        if (_timing.CurTime < _nextAutoCatch)
            return;
        _nextAutoCatch = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        RunAutoCatch();
    }

    /// <summary>
    /// Idle dishes on auto-catch take the nearest grid inside the inner zone of their cone, clear of the warning band,
    /// and lock it in place once both ships are still.
    /// </summary>
    public void RunAutoCatch()
    {
        var query = EntityQueryEnumerator<TractorBeamEmitterComponent, PowerConsumerComponent, TransformComponent>();
        while (query.MoveNext(out var emitter, out var beam, out var power, out var xform))
        {
            if (!beam.AutoCatch || Paused(emitter))
                continue;

            if (beam.Target is { } held)
            {
                if (beam.AutoCaught && !beam.LockedInPlace && !beam.Pulling && beam.SourceGrid is { } holder &&
                    CanLockInPlace(emitter, beam))
                    SetLockedInPlace(emitter, beam, holder, held, true);
                continue;
            }

            if (beam.CooldownRemaining > 0 || beam.AutoCatchConsole is not { } console ||
                !TryComp<TractorBeamConsoleComponent>(console, out var consoleComp) ||
                !ConsoleReady(console, out var source) || xform.GridUid != source || !xform.Anchored ||
                power.ReceivedPower < beam.IdlePower)
                continue;

            if (FindAutoCatchTarget(emitter, beam, console, consoleComp, source) is not { } target)
                continue;

            Capture(emitter, beam, power, console, source, target);
            beam.AutoCaught = true;
        }
    }

    private EntityUid? FindAutoCatchTarget(EntityUid emitter, TractorBeamEmitterComponent beam, EntityUid console,
        TractorBeamConsoleComponent consoleComp, EntityUid source)
    {
        _docked.Clear();
        _shuttles.GetAllDockedShuttlesIgnoringFTLLock(source, _docked);
        var dish = TransformSystem.GetWorldPosition(emitter);
        var forward = TransformSystem.GetWorldRotation(emitter).RotateVec(Vector2.UnitY);
        var sourceCenter = Center(source);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var grids = EntityQueryEnumerator<MapGridComponent, PhysicsComponent>();
        while (grids.MoveNext(out var grid, out _, out var body))
        {
            if (_docked.Contains(grid) || HasComp<ShipShieldedComponent>(grid) || !VisibleTarget(console, source, grid))
                continue;

            var center = Center(grid, body);
            var offset = center - dish;
            if (Vector2.Distance(sourceCenter, center) > consoleComp.Range ||
                !TractorBeamOperatingCone.Contains(offset, forward, beam.MaxRange, beam.ConeHalfAngle) ||
                TractorBeamOperatingCone.IsNearEdge(offset, forward, beam.MaxRange, beam.ConeHalfAngle))
                continue;

            var distance = offset.Length();
            if (distance >= bestDistance)
                continue;
            best = grid;
            bestDistance = distance;
        }

        return best;
    }
}
