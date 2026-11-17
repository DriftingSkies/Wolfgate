using System.Linq;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WG.Planets.Flight;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Shared.Map;

namespace Content.Server._WG.RoundSetup;

public sealed partial class WGCrashScenarioSystem
{
    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateCountdown();
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(0.5);

        UpdateCosmeticBursts();
        var query = EntityQueryEnumerator<WGCrashScenarioComponent>();
        while (query.MoveNext(out var hull, out var crash))
        {
            if (crash.Crashed)
                continue;
            if (HasComp<WGCrashImpactComponent>(hull))
            {
                crash.Crashed = true;
                continue;
            }

            DisableControls(hull);
            if (!crash.Started || crash.Descending || _timing.CurTime < crash.DescendAt)
                continue;

            RemComp<ForceAnchorComponent>(hull);
            RemComp<PreventGridAnchorChangesComponent>(hull);
            _shuttle.Enable(hull, force: true);
            if (_orbit.TryDropFromOrbit(hull, out var reason))
            {
                crash.Descending = true;
                _pa.Announce(hull, Loc.GetString("wg-round-setup-crash-entry"));
            }
            else
            {
                HoldInOrbit(hull);
                Log.Warning($"Crash scenario {crash.LobbyId} could not descend: {reason}");
            }
        }
    }

    /// <summary>Existing station-keeping and anchoring gates suppress premature decay and movement.</summary>
    private void HoldInOrbit(EntityUid hull)
    {
        EnsureComp<ForceAnchorComponent>(hull);
        EnsureComp<PreventGridAnchorChangesComponent>(hull);
        _shuttle.Disable(hull, force: true);
    }

    /// <summary>Stops all lift and helm input, including newly built or repaired machinery.</summary>
    private void DisableControls(EntityUid hull)
    {
        var children = Transform(hull).ChildEnumerator;
        while (children.MoveNext(out var uid))
        {
            if (TryComp<ThrusterComponent>(uid, out var thruster))
                _thrusters.WgDisableCrashThruster(uid, thruster);
            if (!TryComp<ShuttleConsoleComponent>(uid, out var console))
                continue;
            EnsureComp<WGCrashScenarioConsoleComponent>(uid);
            foreach (var pilot in console.SubscribedPilots.ToArray())
                _consoles.RemovePilot(pilot);
        }
    }

    /// <summary>Tracks actual fragments, preserving their admission and descent state.</summary>
    private void OnGridSplit(ref GridSplitEvent args)
    {
        if (!TryComp<WGCrashScenarioComponent>(args.Grid, out var original))
            return;
        foreach (var fragment in args.NewGrids)
        {
            var crash = EnsureComp<WGCrashScenarioComponent>(fragment);
            crash.LobbyId = original.LobbyId;
            crash.DescendAt = original.DescendAt;
            crash.Started = original.Started;
            crash.Descending = original.Descending;
            crash.Crashed = original.Crashed || HasComp<WGCrashImpactComponent>(args.Grid);
            if (!crash.Crashed && !crash.Descending)
                HoldInOrbit(fragment);
            if (_scenarios.TryGetValue(original.LobbyId, out var scenario))
                scenario.Grids.Add(fragment);
        }
    }

    private void OnConsoleOpen(EntityUid uid, WGCrashScenarioConsoleComponent component, ActivatableUIOpenAttemptEvent args)
    {
        if (Transform(uid).GridUid is not { } hull
            || !TryComp<WGCrashScenarioComponent>(hull, out var crash) || crash.Crashed)
            return;
        args.Cancel();
        _popup.PopupEntity(Loc.GetString("wg-round-setup-crash-controls"), uid, args.User);
    }

    /// <summary>A hull still going down has no controls to hop with.</summary>
    private void OnHopAttempt(ref Content.Server._WG.Planets.Hop.WGPlanetHopAttemptEvent args)
    {
        if (!TryComp<WGCrashScenarioComponent>(args.Uid, out var crash) || crash.Crashed)
            return;
        args.Cancelled = true;
        args.Reason = Loc.GetString("wg-round-setup-crash-controls");
    }

    private void OnFTLAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (!TryComp<WGCrashScenarioComponent>(args.Uid, out var crash) || crash.Crashed)
            return;
        args.Cancelled = true;
        args.Reason = Loc.GetString("wg-round-setup-crash-controls");
    }
}
