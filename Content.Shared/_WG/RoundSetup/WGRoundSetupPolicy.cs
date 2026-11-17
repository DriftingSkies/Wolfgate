namespace Content.Shared._WG.RoundSetup;

/// <summary>The result of an authenticated attempt to enter a player lobby.</summary>
public enum WGLobbyJoinDecision : byte
{
    Denied,
    Join,
    Request,
}

/// <summary>Membership limits and access rules shared by scenario lobby operations.</summary>
public static class WGRoundSetupPolicy
{
    /// <summary>Both independent and faction crews use the ship preparation flow.</summary>
    public static bool IsShip(WGRoundScenario scenario) => scenario is WGRoundScenario.Ship or WGRoundScenario.FactionShip;

    /// <summary>Scenarios a player crew can be created with.</summary>
    public static bool IsCrewScenario(WGRoundScenario scenario) =>
        scenario is WGRoundScenario.Ship or WGRoundScenario.Crashlanded or WGRoundScenario.FactionShip or WGRoundScenario.Doomed;

    /// <summary>Formed crews start in the round around characters already playing.</summary>
    public static bool IsFormed(WGRoundScenario scenario) => scenario == WGRoundScenario.Formed;

    /// <summary>Scenarios whose openings use the civilian ship crew jobs and rosters.</summary>
    public static bool UsesShipRoles(WGRoundScenario scenario) => scenario is WGRoundScenario.Ship or WGRoundScenario.Formed;

    /// <summary>Crews a character already in the round can join without respawning.</summary>
    public static bool AllowsLiveMembers(WGRoundScenario scenario, bool started) =>
        scenario == WGRoundScenario.Formed || scenario == WGRoundScenario.Ship && started;

    /// <summary>Access settings that only mean something for a crew flying under a company.</summary>
    public static bool IsCompanyAccess(WGLobbyAccess access) => access is WGLobbyAccess.CompanyOnly or WGLobbyAccess.CompanyFirst;

    /// <summary>Access settings where the host approves requests.</summary>
    public static bool TakesRequests(WGLobbyAccess access) => access is WGLobbyAccess.Requests or WGLobbyAccess.CompanyFirst;

    /// <summary>The longest crew posting.</summary>
    public const int MaximumPostingLength = 200;

    /// <summary>How long a crew waits between postings.</summary>
    public static readonly TimeSpan PostingCooldown = TimeSpan.FromMinutes(10);

    /// <summary>A crew shows as recruiting only while an outsider could actually take one of its openings.</summary>
    public static bool ShowsRecruiting(bool recruiting, WGLobbyAccess access, bool started, bool allowLateJoin, int members, int openings) =>
        recruiting && access is WGLobbyAccess.Public or WGLobbyAccess.Requests or WGLobbyAccess.CompanyFirst && (!started || allowLateJoin) &&
        members < MaximumMembers && openings > 0;

    /// <summary>Accepts one bounded line of plain text for a crew posting.</summary>
    public static bool TryNormalizePosting(string? input, out string posting)
    {
        posting = string.Empty;
        if (input == null || input.Length > MaximumPostingLength * 2)
            return false;
        var normalized = string.Join(' ', input.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length is 0 or > MaximumPostingLength)
            return false;
        for (var index = 0; index < normalized.Length; index++)
        {
            if (char.IsControl(normalized[index]) ||
                char.GetUnicodeCategory(normalized, index) == System.Globalization.UnicodeCategory.Format)
                return false;
        }
        posting = normalized;
        return true;
    }

    /// <summary>The difficulty a scenario's card shows.</summary>
    public static WGScenarioDifficulty Difficulty(WGRoundScenario scenario) => scenario switch
    {
        WGRoundScenario.Crashlanded => WGScenarioDifficulty.VeryHard,
        WGRoundScenario.Doomed => WGScenarioDifficulty.Impossible,
        _ => WGScenarioDifficulty.Normal,
    };

    /// <summary>Faction ships use their own base; civilian ships use the central station.</summary>
    public static bool IsShipStartAllowed(WGShipStartLocation location, WGCrewFaction faction) => faction switch
    {
        WGCrewFaction.None => location is WGShipStartLocation.RandomSpace or WGShipStartLocation.CentralDock,
        WGCrewFaction.TSF or WGCrewFaction.PDV or WGCrewFaction.VG or WGCrewFaction.USSP or WGCrewFaction.Underground => location is WGShipStartLocation.RandomSpace or WGShipStartLocation.FactionBase,
        _ => false,
    };

    /// <summary>The longest public crew description.</summary>
    public const int MaximumDescriptionLength = 512;

    /// <summary>Accepts bounded plain text with paragraphs, without control characters.</summary>
    public static bool TryNormalizeDescription(string? input, out string description)
    {
        description = string.Empty;
        if (input == null)
            return true;
        if (input.Length > MaximumDescriptionLength * 2)
            return false;
        var normalized = input.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (normalized.Length > MaximumDescriptionLength)
            return false;
        for (var index = 0; index < normalized.Length; index++)
        {
            if ((char.IsControl(normalized[index]) && normalized[index] != '\n') ||
                char.GetUnicodeCategory(normalized, index) == System.Globalization.UnicodeCategory.Format)
                return false;
        }
        description = normalized;
        return true;
    }

    /// <summary>The maximum reusable setups stored for one account on this server.</summary>
    public const int MaximumSavedSetups = 20;
    /// <summary>The maximum number of members in one player lobby.</summary>
    public const int MaximumMembers = 16;
    /// <summary>The maximum number of distinct roles offered by one lobby.</summary>
    public const int MaximumRoles = 16;
    /// <summary>The longest custom role label accepted by the setup interface.</summary>
    public const int MaximumRoleNameLength = 64;
    /// <summary>The shortest accepted lobby code.</summary>
    public const int MinimumCodeLength = 6;
    /// <summary>The generated lobby code length and longest accepted code.</summary>
    public const int MaximumCodeLength = 8;
    /// <summary>ASCII letters and digits that avoid zero, one, I and O.</summary>
    public const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Checks access without letting invitations or codes bypass capacity and late-join restrictions.</summary>
    public static WGLobbyJoinDecision EvaluateJoin(
        WGLobbyAccess access,
        bool started,
        bool allowLateJoin,
        bool full,
        bool invited,
        bool validCode,
        bool companyMember = false)
    {
        if (full || started && !allowLateJoin)
            return WGLobbyJoinDecision.Denied;

        return access switch
        {
            WGLobbyAccess.Public => WGLobbyJoinDecision.Join,
            WGLobbyAccess.Requests => invited ? WGLobbyJoinDecision.Join : WGLobbyJoinDecision.Request,
            WGLobbyAccess.Closed => invited ? WGLobbyJoinDecision.Join : WGLobbyJoinDecision.Denied,
            WGLobbyAccess.Code => invited || validCode ? WGLobbyJoinDecision.Join : WGLobbyJoinDecision.Denied,
            WGLobbyAccess.Invite => invited ? WGLobbyJoinDecision.Join : WGLobbyJoinDecision.Denied,
            WGLobbyAccess.CompanyOnly => invited || companyMember ? WGLobbyJoinDecision.Join : WGLobbyJoinDecision.Denied,
            WGLobbyAccess.CompanyFirst => invited || companyMember ? WGLobbyJoinDecision.Join : WGLobbyJoinDecision.Request,
            _ => WGLobbyJoinDecision.Denied,
        };
    }

    /// <summary>Counts the caller's existing reservation without treating a full role as another opening.</summary>
    public static bool CanSelectRole(int capacity, int occupied, bool alreadySelected)
    {
        if (capacity is < 1 or > MaximumMembers || occupied is < 0 or > MaximumMembers)
            return false;
        return alreadySelected ? occupied > 0 && occupied <= capacity : occupied < capacity;
    }

    /// <summary>Accepts bounded plain labels; an empty label uses the underlying job's localized name.</summary>
    public static bool TryNormalizeRoleName(string? input, out string name)
    {
        name = string.Empty;
        if (input == null)
            return true;
        if (input.Length > MaximumRoleNameLength * 2)
            return false;
        foreach (var character in input)
        {
            if (!char.IsLetterOrDigit(character) && " -'.,&/()".IndexOf(character) < 0)
                return false;
        }
        var cleaned = string.Join(' ', input.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > MaximumRoleNameLength)
            return false;
        if (cleaned.Length > 0)
        {
            var hasText = false;
            foreach (var character in cleaned)
                hasText |= char.IsLetterOrDigit(character);
            if (!hasText)
                return false;
        }
        name = cleaned;
        return true;
    }
    /// <summary>Uses the same bounded plain-text rules for a crew's optional public title.</summary>
    public static bool TryNormalizeTitle(string? input, out string title) => TryNormalizeRoleName(input, out title);

    /// <summary>Normalizes ASCII codes while rejecting ambiguous characters and malformed input.</summary>
    public static bool TryNormalizeCode(string? input, out string code)
    {
        code = string.Empty;
        if (input == null)
            return false;

        var trimmed = input.Trim();
        if (trimmed.Length < MinimumCodeLength || trimmed.Length > MaximumCodeLength)
            return false;

        var characters = new char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var character = trimmed[i];
            if (character >= 'a' && character <= 'z')
                character = (char) (character - 'a' + 'A');

            if (CodeAlphabet.IndexOf(character) < 0)
                return false;

            characters[i] = character;
        }

        code = new string(characters);
        return true;
    }
}
