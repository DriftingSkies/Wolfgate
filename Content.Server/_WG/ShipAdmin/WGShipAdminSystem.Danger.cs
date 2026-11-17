using Content.Server._NF.Station.Components;
using Content.Server.StationEvents.Events;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    [Dependency] private LinkedLifecycleGridSystem _lifecycle = default!;

    /// <summary>Brings every player aboard, and whatever they are pulling, to the admin.</summary>
    public bool Eject(ICommonSession admin, EntityUid grid, out string result)
    {
        if (!TryHere(admin, out var here))
        {
            result = Loc.GetString("wg-ship-admin-result-move-no-body");
            return false;
        }

        var moved = BringAboard(grid, here);
        Audit(admin, LogImpact.Medium, grid, $"brought {moved} occupants to themself");
        result = Loc.GetString("wg-ship-admin-result-ejected", ("count", moved), ("ship", Name(grid)));
        return true;
    }

    /// <summary>
    /// Makes the ship a bare unclaimed hull, as one on the used-ship lot is: owners, deeds, access, crew slots and its
    /// late-join station all go. What is aboard stays.
    /// </summary>
    public bool Strip(ICommonSession admin, EntityUid grid, out string result)
    {
        if (!CanBeOwned(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-not-ship");
            return false;
        }

        var name = Name(grid);
        var owner = OwnerName(grid);
        DeleteShipStation(grid);
        _shipyard.RevokeDeeds(grid);
        _shipyard.StripForResale(grid);
        Audit(admin, LogImpact.High, grid, $"stripped it of its owner '{owner}', deeds, access and station");
        result = Loc.GetString("wg-ship-admin-result-stripped", ("ship", name));
        return true;
    }

    /// <summary>
    /// Deletes a ship. Players aboard are never deleted with it: they come to the admin when asked to, otherwise they
    /// are left where the ship was.
    /// </summary>
    public bool Delete(ICommonSession admin, EntityUid grid, bool bringOccupants, out string result)
    {
        if (!CanBeOwned(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-not-ship");
            return false;
        }

        var name = Name(grid);
        var owner = OwnerName(grid);
        var moved = 0;
        if (bringOccupants)
        {
            if (!TryHere(admin, out var here))
            {
                result = Loc.GetString("wg-ship-admin-result-move-no-body");
                return false;
            }

            moved = BringAboard(grid, here);
        }

        Audit(admin, LogImpact.High, grid, $"deleted it (owner '{owner}'); occupants brought to the admin: {moved}");
        _fixes.Remove(grid);
        DeleteShipStation(grid);
        _shipyard.RevokeDeeds(grid);
        _docking.UndockDocks(grid);
        _lifecycle.UnparentPlayersFromGrid(grid, true);
        result = Loc.GetString("wg-ship-admin-result-deleted", ("ship", name));
        return true;
    }

    /// <summary>Where the admin stands, as a spot on their map.</summary>
    private bool TryHere(ICommonSession admin, out EntityCoordinates here)
    {
        here = default;
        if (admin.AttachedEntity is not { Valid: true } body || Transform(body).MapUid is not { } map)
            return false;

        here = new EntityCoordinates(map, _transform.GetWorldPosition(body));
        return true;
    }

    private int BringAboard(EntityUid grid, EntityCoordinates here)
    {
        // Characters, borgs and anything with a player's mind, with what they pull; then any body a player is in now.
        var aboard = new HashSet<EntityUid>();
        foreach (var (entity, _, _) in _lifecycle.GetEntitiesToReparent(grid))
            aboard.Add(entity.Owner);

        foreach (var session in _player.Sessions)
        {
            if (session.AttachedEntity is { } body && !HasComp<GhostComponent>(body) && Transform(body).GridUid == grid)
                aboard.Add(body);
        }

        foreach (var uid in aboard)
            _transform.SetCoordinates(uid, here);

        return aboard.Count;
    }

    /// <summary>Deletes the late-join station a ship was given at purchase. A real station the grid belongs to is left alone.</summary>
    private void DeleteShipStation(EntityUid grid)
    {
        if (_station.GetOwningStation(grid) is { } station && CompOrNull<ExtraShuttleInformationComponent>(station)?.Vessel != null)
            _station.DeleteStation(station);
    }
}
