using Content.Server._WG.BoardingAction.Components;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    /// <summary>Dampening under a lockdown, four times park: engines can only push the ship a quarter as fast.</summary>
    private const float WgLockdownDamping = AnchorDampingStrength * 4f;

    /// <summary>Whether a lockdown has this ship's dampening, against every pilot, autopilot and crew.</summary>
    private bool WgLockedDown(EntityUid grid)
    {
        return TryComp<WGHackedGridComponent>(grid, out var hacked) && hacked.LockedOut.Count > 0;
    }

    /// <summary>Clamps a ship's dampening past park for a lockdown. Let go, the ship is left in park.</summary>
    public void WgSetLockdownDamping(EntityUid grid, bool clamped)
    {
        if (TerminatingOrDeleted(grid) || !TryComp<ShuttleComponent>(grid, out var shuttle))
            return;

        shuttle.BodyModifier = clamped ? WgLockdownDamping : AnchorDampingStrength;
        if (shuttle.DampingModifier != 0)
            shuttle.DampingModifier = shuttle.BodyModifier;

        _console.RefreshShuttleConsoles(grid);
    }

    /// <summary>Whether a ship is held from leaving. Every jump passes here, whatever started it.</summary>
    private bool WgDisruptedDeparture(EntityUid uid) => HasComp<WGFtlDisruptedComponent>(uid);

    /// <summary>Stops a jump still spooling up and leaves the ship where it is, ready to fly.</summary>
    public bool WgAbortFtlSpool(EntityUid uid)
    {
        if (!TryComp<FTLComponent>(uid, out var ftl) || ftl.State != FTLState.Starting || ftl.LinkedShuttle != null)
            return false;

        ftl.StartupStream = _audio.Stop(ftl.StartupStream);
        if (TryComp<ShuttleComponent>(uid, out var shuttle))
            _thruster.DisableLinearThrusters(shuttle);

        RemComp<FTLComponent>(uid);
        _console.RefreshShuttleConsoles(uid);
        return true;
    }
}
