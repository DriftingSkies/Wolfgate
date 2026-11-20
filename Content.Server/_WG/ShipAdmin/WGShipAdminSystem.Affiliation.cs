using System.Linq;
using Content.Shared._Mono.Company;
using Content.Shared._WG.Companies;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Database;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    /// <summary>Every player company, for the panel's company picker.</summary>
    public List<WGShipAdminCompany> ListCompanies()
    {
        return _companies.ListCompanies()
            .Select(company => new WGShipAdminCompany { Id = company.Id, Name = company.Name, Tag = company.Tag })
            .OrderBy(company => company.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Sets the faction the hull flies for, or none. A ship flying for a player company keeps the faction aside,
    /// as that module does, and gets it back when it stops.
    /// </summary>
    public bool SetHullFaction(ICommonSession admin, EntityUid grid, string? faction, out string result)
    {
        ProtoId<CompanyPrototype>? picked = null;
        if (!string.IsNullOrEmpty(faction))
        {
            if (!_proto.HasIndex<CompanyPrototype>(faction))
            {
                result = Loc.GetString("wg-ship-admin-result-bad-faction");
                return false;
            }

            picked = faction;
        }

        var before = CurrentFaction(grid);
        if (TryComp<WGCompanyShipComponent>(grid, out var flown))
        {
            // Empty, not null, records a faction staff cleared: null means none was set aside yet, and the
            // company sync would then set aside whatever the hull still carries.
            flown.PreviousFaction = picked?.Id ?? string.Empty;
        }
        else
        {
            SetFaction(grid, picked);
        }

        if (TryComp<WGShipAccessComponent>(grid, out var access)
            && access.Mode == WGShipAccessMode.Faction
            && !_accessRules.IsFactionGrid(grid, out _))
            _shipAccess.SetMode((grid, access), WGShipAccessMode.Private);

        Audit(admin, LogImpact.Medium, grid, $"set its hull faction from '{before}' to '{picked?.Id}'");
        var name = picked is { } id ? FactionName(id) : Loc.GetString("wg-ship-admin-none");
        result = Loc.GetString(flown != null ? "wg-ship-admin-result-faction-company-ship" : "wg-ship-admin-result-faction",
            ("ship", Name(grid)), ("faction", name));
        return true;
    }

    /// <summary>Flies the ship for a player company, or takes it out of the one it flies for.</summary>
    public bool SetShipCompany(ICommonSession admin, EntityUid grid, int? company, out string result)
    {
        if (!CanBeOwned(grid))
        {
            result = Loc.GetString("wg-ship-admin-result-not-ship");
            return false;
        }

        var before = CompOrNull<WGCompanyShipComponent>(grid)?.Name;
        if (!_companies.SetShipCompany(grid, company))
        {
            result = Loc.GetString("wg-ship-admin-result-bad-company");
            return false;
        }

        var after = CompOrNull<WGCompanyShipComponent>(grid)?.Name;
        Audit(admin, LogImpact.Medium, grid, $"set its player company from '{before}' to '{after}'");
        result = after == null
            ? Loc.GetString("wg-ship-admin-result-company-cleared", ("ship", Name(grid)))
            : Loc.GetString("wg-ship-admin-result-company", ("ship", Name(grid)), ("company", after));
        return true;
    }

    /// <summary>
    /// The hull's faction: its own, or the one a company ship set aside. Until the company sync has set it aside,
    /// the hull still carries it.
    /// </summary>
    private string CurrentFaction(EntityUid grid)
    {
        if (CompOrNull<WGCompanyShipComponent>(grid)?.PreviousFaction is { } setAside)
            return setAside == "None" ? string.Empty : setAside;

        return TryComp<CompanyComponent>(grid, out var hull) && hull.CompanyName.Id is { Length: > 0 } id && id != "None"
            ? id
            : string.Empty;
    }

    private string FactionName(string id)
    {
        return _proto.TryIndex<CompanyPrototype>(id, out var proto) ? Loc.GetString(proto.Name) : id;
    }
}
