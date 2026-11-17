using Content.Shared._WG.ShipAccess;
using Content.Shared.Database;
using Robust.Shared.Network;

namespace Content.Server._WG.ShipAccess;

/// <summary>Takes owners off a ship and renames its owner, for the admin ship panel.</summary>
public sealed partial class WGShipAccessServerSystem
{
    /// <summary>The ship's access component, set up as an unlocked or deed-locked ship when it had none.</summary>
    public Entity<WGShipAccessComponent> EnsureAccess(EntityUid grid)
    {
        return EnsureShip(grid);
    }

    /// <summary>Unregisters one player's account; false when the ship was not registered to it.</summary>
    public bool RemoveOwnerUser(Entity<WGShipAccessComponent> ship, NetUserId user)
    {
        if (!ship.Comp.OwnerUsers.Remove(user))
            return false;

        Dirty(ship);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(ship.Owner):grid} was unregistered from player {user}");
        return true;
    }

    /// <summary>Unregisters every account and forgets the buyer's stand-in card, so only deeds still own the ship.</summary>
    public void ClearOwners(Entity<WGShipAccessComponent> ship)
    {
        if (ship.Comp.OwnerUsers.Count == 0 && ship.Comp.BuyerKey == null)
            return;

        ship.Comp.OwnerUsers.Clear();
        ship.Comp.BuyerKey = null;
        Dirty(ship);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(ship.Owner):grid} was unregistered from every player");
    }

    /// <summary>Sets the owner name the access tab shows. It grants nothing.</summary>
    public void SetOwnerName(Entity<WGShipAccessComponent> ship, string name)
    {
        if (ship.Comp.OwnerName == name)
            return;

        ship.Comp.OwnerName = name;
        Dirty(ship);
    }

    /// <summary>
    /// Call after a ship's owners changed: rewrites its readers for the new owner keys. A locked ship left with no
    /// owner key is unlocked, so nobody is shut out, and locks itself again once an owner's card can open it.
    /// </summary>
    /// <returns>True when the ship had to be unlocked.</returns>
    public bool OwnersChanged(Entity<WGShipAccessComponent> ship)
    {
        if (!ship.Comp.Locked || CanLock(ship))
        {
            RefreshShip(ship);
            return false;
        }

        SetLocked(ship, false);
        if (ship.Comp.OwnerUsers.Count > 0)
            _lockWhenKeyed.Add(ship);

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(ship.Owner):grid} was unlocked because no owner's card could open it");
        return true;
    }
}
