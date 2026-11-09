using System.Linq;
using Content.Shared._WF.Companies;

namespace Content.Server._WF.Companies;

/// <summary>What the Colonies module needs of companies: colony income, member counts and yard licences.</summary>
public sealed partial class WFCompanySystem
{
    /// <summary>Pays a colony's income for a company's buildings into its account.</summary>
    public bool PayColonyIncome(int companyId, long amount, string world)
    {
        if (amount <= 0 || !_companies.TryGetValue(companyId, out var company))
            return false;
        if (!Transact(company, WFCompanyLedgerKind.Colony, amount, null, world, Loc.GetString("wf-company-ledger-colony-note", ("world", world)),
                ignoreFrozen: true))
            return false;
        Touch(company);
        return true;
    }

    /// <summary>How many players have characters in a company, or 0 for none.</summary>
    public int MemberPlayers(int companyId) => _companies.TryGetValue(companyId, out var company) ? PlayerCount(company) : 0;

    /// <summary>Whether a company holds a fleet licence for a vessel.</summary>
    public bool HasLicence(int companyId, string vessel) =>
        _companies.TryGetValue(companyId, out var company) && company.Licences.Any(licence => licence.Vessel == vessel);

    /// <summary>Grants a company a fleet licence for a vessel at no cost, as a yard build's first completion does.</summary>
    public bool GrantLicence(int companyId, string vessel)
    {
        if (!_companies.TryGetValue(companyId, out var company) || company.Licences.Any(licence => licence.Vessel == vessel))
            return false;
        var licence = new Licence { Vessel = vessel };
        company.Licences.Add(licence);
        Track(AddLicenceRow(company, licence, 0));
        return true;
    }

    /// <summary>The company a session's character belongs to, with its name, or null.</summary>
    public (int Id, string Name)? CompanyOfSession(Robust.Shared.Player.ICommonSession session) =>
        CompanyIdOf(session) is { } id && _companies.TryGetValue(id, out var company) ? (id, company.Name) : null;
}
