using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    /// <summary>Locks or unlocks a ship's doors and lockers. A lock that no owner's card could open is refused.</summary>
    public bool SetLocked(ICommonSession admin, EntityUid grid, bool locked, bool notify, out string result)
    {
        if (!TryAccess(grid, out var access, out result))
            return false;

        if (locked && !_shipAccess.CanLock(access))
        {
            result = Loc.GetString("wg-ship-admin-result-lock-refused");
            return false;
        }

        _shipAccess.SetLocked(access, locked);
        Audit(admin, LogImpact.Medium, grid, locked ? "locked its doors" : "unlocked its doors");
        result = Told(grid, notify, Loc.GetString(locked ? "wg-ship-admin-result-locked" : "wg-ship-admin-result-unlocked", ("ship", Name(grid))));
        return true;
    }

    /// <summary>
    /// Sets the access mode. Crew and Company only work on a ship already linked to one; the panel links none, so a
    /// ship is never bound to the admin's own crew or company.
    /// </summary>
    public bool SetMode(ICommonSession admin, EntityUid grid, WGShipAccessMode mode, bool notify, out string result)
    {
        if (!TryAccess(grid, out var access, out result))
            return false;

        var modeName = Loc.GetString($"wg-ship-admin-mode-{mode.ToString().ToLowerInvariant()}");
        if (!_shipAccess.SetMode(access, mode))
        {
            result = Loc.GetString("wg-ship-admin-result-mode-unavailable", ("mode", modeName));
            return false;
        }

        Audit(admin, LogImpact.Medium, grid, $"set its access mode to {mode}");
        result = Told(grid, notify, Loc.GetString("wg-ship-admin-result-mode", ("ship", Name(grid)), ("mode", modeName)));
        return true;
    }

    /// <summary>Puts the ID card a connected player carries on the allow list.</summary>
    public bool AllowAdd(ICommonSession admin, EntityUid grid, NetUserId user, bool notify, out string result)
    {
        if (!TryAccess(grid, out var access, out result))
            return false;

        if (!_player.TryGetSessionById(user, out var session) || session.AttachedEntity is not { Valid: true } body || HasComp<GhostComponent>(body))
        {
            result = Loc.GetString("wg-ship-admin-result-allow-no-body");
            return false;
        }

        if (!_accessRules.TryGetCard(body, out var card))
        {
            result = Loc.GetString("wg-ship-admin-result-allow-no-card", ("name", Name(body)));
            return false;
        }

        if (!_accessRules.TryGetKey(card, out _))
        {
            result = Loc.GetString("wg-ship-admin-result-allow-no-record", ("name", Name(body)));
            return false;
        }

        if (!_shipAccess.TryAddPerson(access, body, Loc.GetString("wg-ship-admin-label-staff")))
        {
            result = Loc.GetString("wg-ship-admin-result-allow-already", ("name", Name(body)));
            return false;
        }

        Audit(admin, LogImpact.Medium, grid, $"added {Name(body)} ({session.Name}) to the allow list");
        result = Told(grid, notify, Loc.GetString("wg-ship-admin-result-allow-added", ("name", Name(body)), ("ship", Name(grid))));
        return true;
    }

    /// <summary>Takes one card off the allow list and off every door's list.</summary>
    public bool AllowRemove(ICommonSession admin, EntityUid grid, WGShipAccessKey key, bool notify, out string result)
    {
        if (!TryAccess(grid, out var access, out result))
            return false;

        var name = _accessRules.TryGetEntry(access.Comp, key, out var entry) ? entry.Name : string.Empty;
        if (!_shipAccess.RemoveEntry(access, key))
        {
            result = Loc.GetString("wg-ship-admin-result-allow-missing");
            return false;
        }

        Audit(admin, LogImpact.Medium, grid, $"removed {name} from the allow list");
        result = Told(grid, notify, Loc.GetString("wg-ship-admin-result-allow-removed", ("name", name), ("ship", Name(grid))));
        return true;
    }

    /// <summary>Empties the allow list.</summary>
    public bool AllowClear(ICommonSession admin, EntityUid grid, bool notify, out string result)
    {
        if (!TryAccess(grid, out var access, out result))
            return false;

        var count = _shipAccess.ClearAllowList(access);
        Audit(admin, LogImpact.Medium, grid, $"cleared the allow list ({count} entries)");
        result = Told(grid, notify, Loc.GetString("wg-ship-admin-result-allow-cleared", ("count", count), ("ship", Name(grid))));
        return true;
    }

    /// <summary>Puts every door back on the default rule and clears the ship's door code.</summary>
    public bool ResetDoors(ICommonSession admin, EntityUid grid, bool notify, out string result)
    {
        if (!TryAccess(grid, out var access, out result))
            return false;

        var doors = _shipAccess.SetAllDoorRules(access, WGDoorAccessRule.Default);
        _shipAccess.SetShipCode(access, null);
        Audit(admin, LogImpact.Medium, grid, $"reset its door rules ({doors} doors) and cleared its ship code");
        result = Told(grid, notify, Loc.GetString("wg-ship-admin-result-doors-reset", ("count", doors), ("ship", Name(grid))));
        return true;
    }

    /// <summary>
    /// The ship's access, set up when a deeded ship had none. A grid with neither is refused: setting access up on a
    /// station would rewrite every door reader aboard. Giving it an owner is the deliberate way to do that.
    /// </summary>
    private bool TryAccess(EntityUid grid, out Entity<WGShipAccessComponent> access, out string result)
    {
        access = default;
        result = string.Empty;
        if (!HasComp<WGShipAccessComponent>(grid) && !HasComp<ShuttleDeedComponent>(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-no-access");
            return false;
        }

        access = _shipAccess.EnsureAccess(grid);
        return true;
    }

    /// <summary>Tells the ship's owners their access changed, when asked to, and adds who could not be told to the result.</summary>
    private string Told(EntityUid grid, bool notify, string result)
    {
        if (!notify)
            return result;

        var notes = new List<string>();
        NoteUntold(notes, Tell(OwnerAccounts(grid), Loc.GetString("wg-ship-admin-notice-access", ("ship", Name(grid)))));
        return WithNotes(result, notes);
    }
}
