using System.Linq;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Audio;

namespace Content.Server.Shuttles.Systems;

/// <summary>
/// Lets whoever manages a ship without holding its deed card work the helm lock: a player the ship is registered
/// to, a holder of an owner's card, or an admin. The upstream lock only knows deed cards and vouchers.
/// </summary>
public sealed partial class ShuttleConsoleLockSystem
{
    [Dependency] private WGShipAccessSystem _wgAccessRules = default!;

    private static readonly SoundSpecifier WgSwipeSound = new SoundPathSpecifier("/Audio/Machines/id_swipe.ogg");

    /// <summary>
    /// Locks a ship's helm to its deed and takes its pilots off, for a ship left with nobody to lock it.
    /// </summary>
    /// <returns>False when the ship has no deed to lock to.</returns>
    public bool WgLockHelm(EntityUid grid)
    {
        if (!TryComp<ShipGridLockComponent>(grid, out var gridLock) || string.IsNullOrEmpty(gridLock.ShuttleId))
            return false;

        if (!gridLock.Locked)
            SetGridLockState(grid, true);

        // A lock only seats nobody new; whoever was flying, at any console, is still at the controls.
        var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out _, out var console, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            foreach (var pilot in console.SubscribedPilots.ToList())
                _consoleSystem.RemovePilot(pilot);
        }

        return true;
    }

    /// <summary>Whether the user manages the ship this console is on.</summary>
    private bool WgIsManager(EntityUid console, EntityUid user)
    {
        return Transform(console).GridUid is { } grid && _wgShipAccess.IsOwner(user, grid);
    }

    /// <summary>
    /// Toggles the helm lock for a manager who holds no deed for the ship. A deed holder is left to the swipe below,
    /// and so is anyone else, who gets the wrong-deed popup or guest access from it.
    /// </summary>
    /// <returns>True when the toggle was handled here.</returns>
    private bool WgTryManagerToggle(EntityUid console, EntityUid user, ShuttleConsoleLockComponent lockComp, bool locked)
    {
        if (Transform(console).GridUid is not { } grid || _wgAccessRules.HasDeedFor(user, grid) || !_wgShipAccess.IsOwner(user, grid))
            return false;

        var gridLock = CompOrNull<ShipGridLockComponent>(grid);
        var shuttleId = gridLock?.ShuttleId ?? lockComp.ShuttleId;

        // A console with no ship behind it can't be locked; the swipe below says so.
        if (!locked && string.IsNullOrEmpty(shuttleId))
            return false;

        if (gridLock != null)
            SetGridLockState(grid, !locked, locked ? null : shuttleId);
        else
            lockComp.Locked = !locked;

        _audio.PlayPvs(WgSwipeSound, console);
        Popup.PopupEntity(Loc.GetString(locked ? "shuttle-console-unlocked" : "shuttle-console-locked-success"), console);
        if (locked || !TryComp<ShuttleConsoleComponent>(console, out var shuttleComp))
            return true;

        foreach (var pilot in shuttleComp.SubscribedPilots.ToList())
            _consoleSystem.RemovePilot(pilot);

        return true;
    }
}
