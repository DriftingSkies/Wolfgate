using System.Linq;
using Content.Shared._WG.ShipAccess;

namespace Content.Server._WG.ShipAccess;

public sealed partial class WGShipAccessServerSystem
{
    /// <summary>Replaces automatic crew credentials without changing manually granted access.</summary>
    public void SetCrew(Entity<WGShipAccessComponent> ship, int? id, string? name, IEnumerable<WGShipAccessKey> keys)
    {
        var current = keys.Distinct().ToList();
        if (ship.Comp.CrewId == id && ship.Comp.CrewName == name && ship.Comp.CrewKeys.SequenceEqual(current))
            return;
        ship.Comp.CrewId = id;
        ship.Comp.CrewName = name;
        ship.Comp.CrewKeys = current;
        Dirty(ship);
        RefreshShip(ship);
    }

    /// <summary>Replaces the linked company's credentials without changing manually granted access.</summary>
    public void SetCompany(Entity<WGShipAccessComponent> ship, int? id, string? name, IEnumerable<WGShipAccessKey> keys)
    {
        var current = keys.Distinct().ToList();
        if (ship.Comp.CompanyId == id && ship.Comp.CompanyName == name && ship.Comp.CompanyKeys.SequenceEqual(current))
            return;
        ship.Comp.CompanyId = id;
        ship.Comp.CompanyName = name;
        ship.Comp.CompanyKeys = current;
        if (id == null && ship.Comp.Mode == WGShipAccessMode.Company)
            ship.Comp.Mode = WGShipAccessMode.Private;
        Dirty(ship);
        RefreshShip(ship);
    }

    /// <summary>Links the ship to the actor's player company through the company system.</summary>
    public bool TryLinkCompany(Entity<WGShipAccessComponent> ship, EntityUid actor)
    {
        var ev = new WGShipCompanyLinkEvent(actor);
        RaiseLocalEvent(ship.Owner, ref ev);
        return ship.Comp.CompanyId != null;
    }

    /// <summary>Links an unbound ship to the actor's crew through its membership system.</summary>
    public bool TryLinkCrew(Entity<WGShipAccessComponent> ship, EntityUid actor)
    {
        if (ship.Comp.CrewId != null)
            return true;
        var ev = new WGShipCrewLinkEvent(actor);
        RaiseLocalEvent(ship.Owner, ref ev);
        return ship.Comp.CrewId != null;
    }

    /// <summary>Changes ship mode only when its required crew or company exists.</summary>
    public bool SetMode(Entity<WGShipAccessComponent> ship, WGShipAccessMode mode)
    {
        if (!Enum.IsDefined(mode) || mode == WGShipAccessMode.Crew && ship.Comp.CrewId == null ||
            mode == WGShipAccessMode.Company && ship.Comp.CompanyId == null ||
            mode == WGShipAccessMode.Faction && !_access.IsFactionGrid(ship, out _))
            return false;
        if (ship.Comp.Mode == mode)
            return true;
        ship.Comp.Mode = mode;
        Dirty(ship);
        RefreshShip(ship);
        return true;
    }
}

/// <summary>Asks the company system to bind a ship to the actor's player company.</summary>
[ByRefEvent]
public readonly record struct WGShipCompanyLinkEvent(EntityUid Actor);

/// <summary>Asks the crew membership system to bind a ship to the actor's current crew.</summary>
[ByRefEvent]
public readonly record struct WGShipCrewLinkEvent(EntityUid Actor);
