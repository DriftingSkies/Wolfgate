using Content.Server.Database;

namespace Content.Server._WG.Companies;

/// <summary>A company as loaded from the database, with its members and newest ledger rows.</summary>
public sealed record WGStoredCompany(WolfgateCompany Company, List<WGStoredMember> Members, List<WolfgateCompanyLedger> Ledger,
    List<WolfgateCompanyLicence> Licences, List<WGStoredApplication> Applications)
{
    /// <summary>How many of a company's newest ledger rows the server keeps in memory.</summary>
    public const int LedgerCache = 200;
}

/// <summary>A membership row joined with the character it belongs to.</summary>
public sealed record WGStoredMember(int ProfileId, int CompanyId, int? RankId, DateTime JoinedAt, int UnpaidSeconds,
    int WithdrawnToday, DateTime WithdrawDay, Guid UserId, string CharacterName, int Slot);

/// <summary>An application row joined with the character who sent it.</summary>
public sealed record WGStoredApplication(int CompanyId, int ProfileId, int? RankId, string Message, Guid UserId, string CharacterName,
    int Slot);
