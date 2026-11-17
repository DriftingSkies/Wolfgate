using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Companies;

/// <summary>Operations on the company service; the acting character always comes from the sender's session.</summary>
[Serializable, NetSerializable]
public enum WGCompanyAction : byte
{
    Refresh, Create, Invite, AcceptInvite, DeclineInvite, Apply, CancelApplication, AcceptApplication, RejectApplication,
    Leave, Kick, SetMemberRank, UpsertRank, RemoveRank, MoveRank, EditDetails, Rename, SetCut, Transfer, Disband,
    Deposit, Withdraw, JoinAsRank, SetLogo, RequestLogo, SetRankIcon, RequestRankIcon, BuyLicence, SellLicence, Launch, SetLaunchFee, Pay,
    SetRankLoadout, SetOpenRank, JoinOpen, SetPrivate, RequestProfile, SetLicenceName,
}

/// <summary>A request from the Company window.</summary>
[Serializable, NetSerializable]
public sealed class WGCompanyRequest(WGCompanyAction action) : EntityEventArgs
{
    public WGCompanyAction Action = action;
    public int CompanyId;
    /// <summary>A member or applicant, by character profile id.</summary>
    public int? Target;
    /// <summary>An online player to invite.</summary>
    public NetUserId? TargetUser;
    public int? RankId;
    public string? Name;
    public string? Tag;
    public string? Description;
    public Color Colour = Color.White;
    public bool Recruiting;
    public WGCompanyPermissions Permissions;
    public int Wage;
    public int WithdrawCap;
    public string? BaseJob;
    public int Amount;
    /// <summary>MoveRank direction: true moves the rank up one place.</summary>
    public bool Up;
    /// <summary>SetLogo: raw RGBA pixels, or null to clear the logo.</summary>
    public byte[]? Logo;
    /// <summary>BuyLicence: the vessel prototype id.</summary>
    public string? Vessel;
    /// <summary>SellLicence and Launch: the licence.</summary>
    public int LicenceId;
    /// <summary>SetRankLoadout: loadout prototype ids bought for the rank at each spawn.</summary>
    public string[]? Loadouts;
    /// <summary>SetPrivate: whether only members see who is in the company.</summary>
    public bool Private;
}

[Serializable, NetSerializable]
public sealed record WGCompanyListing(int Id, string Name, string Tag, string Description, Color Colour, string OwnerName,
    int Members, bool Recruiting, int LogoVersion = 0, long Value = 0, WGCompanyOpenRank[]? Ranks = null, int? OpenRankId = null, bool Private = false);

/// <summary>A rank an applicant can ask for.</summary>
[Serializable, NetSerializable]
public sealed record WGCompanyOpenRank(int Id, string Title, string BaseJob);

[Serializable, NetSerializable]
public sealed record WGCompanyRank(int Id, int Order, string Title, WGCompanyPermissions Permissions, int Wage, int WithdrawCap,
    string BaseJob, int IconVersion = 0, string[]? Loadouts = null);

[Serializable, NetSerializable]
public sealed record WGCompanyMember(int Profile, string Name, int? Rank, bool Online);

[Serializable, NetSerializable]
public sealed record WGCompanyApplicant(int Profile, string Name, int? RankId = null, string Message = "");

[Serializable, NetSerializable]
public sealed record WGCompanyPlayer(NetUserId User, string Name);

/// <summary>A company one of the player's characters belongs to, for the character creator.</summary>
[Serializable, NetSerializable]
public sealed record WGCompanyCharacter(int Slot, string Company, string Tag, string Rank, int CompanyId = 0, int LogoVersion = 0,
    int RankId = 0, int IconVersion = 0);

/// <summary>A fleet licence and whether it has been launched this round.</summary>
[Serializable, NetSerializable]
public sealed record WGCompanyLicence(int Id, string Vessel, string Name, int ShipyardPrice, bool Launched, string ShipName = "");

/// <summary>A design the owner can license, with what the licence costs.</summary>
[Serializable, NetSerializable]
public sealed record WGCompanyLicenceOffer(string Vessel, string Name, long Price);

[Serializable, NetSerializable]
public sealed record WGCompanyLedgerEntry(DateTime Time, WGCompanyLedgerKind Kind, long Amount, long BalanceAfter,
    string Actor, string Note);

/// <summary>The acting character's own company, with what their rank may see.</summary>
[Serializable, NetSerializable]
public sealed class WGCompanyDetail
{
    public int Id;
    public string Name = string.Empty;
    public string Tag = string.Empty;
    public string Description = string.Empty;
    public Color Colour = Color.White;
    public int OwnerProfile;
    public string OwnerName = string.Empty;
    public long Balance;
    public int Cut;
    public bool Recruiting;
    public bool Frozen;
    public bool ArtLocked;
    public int LogoVersion;
    public int LaunchFee;
    /// <summary>The rank anyone may join at directly, if open access is on.</summary>
    public int? OpenRankId;
    /// <summary>Only members see who is in the company.</summary>
    public bool Private;
    /// <summary>How many company ships are out this round, which sets the next registry fee.</summary>
    public int LaunchedThisRound;
    public WGCompanyLicence[] Licences = Array.Empty<WGCompanyLicence>();
    /// <summary>Owner only: designs that can be licensed.</summary>
    public WGCompanyLicenceOffer[] LicenceOffers = Array.Empty<WGCompanyLicenceOffer>();
    public WGCompanyRank[] Ranks = Array.Empty<WGCompanyRank>();
    /// <summary>The owner's own rank: title, base job and icon, with the company id negated as its id.</summary>
    public WGCompanyRank? OwnerRank;
    public WGCompanyMember[] Members = Array.Empty<WGCompanyMember>();
    public WGCompanyApplicant[] Applications = Array.Empty<WGCompanyApplicant>();
    public int YourProfile;
    public int? YourRank;
    public WGCompanyPermissions YourPermissions;
    public int WithdrawnToday;
    /// <summary>Played seconds counted towards the next wage.</summary>
    public int UnpaidSeconds;
    /// <summary>Newest first: the whole ledger for ranks that handle money, otherwise only the member's own rows.</summary>
    public WGCompanyLedgerEntry[] Ledger = Array.Empty<WGCompanyLedgerEntry>();
}

/// <summary>A company's logo pixels, sent when a client asks for a version it lacks.</summary>
[Serializable, NetSerializable]
public sealed class WGCompanyLogoEvent(int companyId, int version, byte[]? logo) : EntityEventArgs
{
    public int CompanyId = companyId;
    public int Version = version;
    public byte[]? Logo = logo;
}

/// <summary>A rank's job icon pixels, sent on request and to everyone when it changes.</summary>
[Serializable, NetSerializable]
public sealed class WGCompanyRankIconEvent(int rankId, int version, byte[]? icon) : EntityEventArgs
{
    public int RankId = rankId;
    public int Version = version;
    public byte[]? Icon = icon;
}

/// <summary>One member in a company's profile, with the rank icon to draw.</summary>
[Serializable, NetSerializable]
public sealed record WGCompanyProfileMember(string Name, string Rank, int RankId, int IconVersion, string BaseJob);

/// <summary>A ship in a company's hangar, as anyone can see it.</summary>
[Serializable, NetSerializable]
public sealed record WGCompanyProfileShip(string Design, string Name, bool Launched);

/// <summary>A company's public profile; members are left out when the company is private and the viewer isn't in it.</summary>
[Serializable, NetSerializable]
public sealed class WGCompanyProfileEvent(int companyId, bool hidden, WGCompanyProfileMember[] members, WGCompanyProfileShip[] ships) : EntityEventArgs
{
    public int CompanyId = companyId;
    public bool Hidden = hidden;
    public WGCompanyProfileMember[] Members = members;
    public WGCompanyProfileShip[] Ships = ships;
}

/// <summary>A personal snapshot of the company service for the sender's current character.</summary>
[Serializable, NetSerializable]
public sealed class WGCompanyState : EntityEventArgs
{
    public bool Loaded;
    /// <summary>The character the window acts as; null when the player has none loaded.</summary>
    public string? CharacterName;
    public bool CanJoin;
    /// <summary>The member can late-join the round as their rank now.</summary>
    public bool CanJoinRound;
    public int? CharacterBalance;
    /// <summary>What creating a company costs, and the hours of play it needs.</summary>
    public int CreateFee;
    public int CreateHours;
    /// <summary>The player's total hours played, once loaded.</summary>
    public int? PlayedHours;
    public WGCompanyListing[] Directory = Array.Empty<WGCompanyListing>();
    public WGCompanyListing[] Invitations = Array.Empty<WGCompanyListing>();
    public int[] Applications = Array.Empty<int>();
    public WGCompanyDetail? Own;
    public WGCompanyPlayer[] InviteTargets = Array.Empty<WGCompanyPlayer>();
    /// <summary>Memberships of all the player's characters, by slot.</summary>
    public WGCompanyCharacter[] Characters = Array.Empty<WGCompanyCharacter>();
    public string? Message;
}
