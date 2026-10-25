using Content.Shared._WF.MappingMode;
using Robust.Shared.Network;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// A shared mapping session: its members, the observers of those present, and the map its z-stack grows from.
/// </summary>
public sealed class WFMappingSession(int id, string code, string name, NetUserId owner, EntityUid rootMap)
{
    public readonly int Id = id;
    /// <summary>The join code; the owner can replace it.</summary>
    public string Code = code;
    public string Name = name;
    public NetUserId Owner = owner;

    /// <summary>Members, with the time each joined; absent members stay members until they leave.</summary>
    public readonly Dictionary<NetUserId, TimeSpan> Members = new();

    /// <summary>Players removed from the session, who can't join it again.</summary>
    public readonly HashSet<NetUserId> Kicked = new();

    /// <summary>The observer of each member in the session right now.</summary>
    public readonly Dictionary<NetUserId, EntityUid> Present = new();

    /// <summary>The ground level; the session's other levels share its z-network.</summary>
    public EntityUid RootMap = rootMap;

    /// <summary>Changed since the last save.</summary>
    public bool Dirty;

    /// <summary>UTC time of the last save into a library.</summary>
    public DateTime? LastSaved;

    /// <summary>The autosave step the owner picked, in minutes; 0 is off.</summary>
    public int AutosaveMinutes;

    /// <summary>Game time the session is next checked for an autosave.</summary>
    public TimeSpan NextAutosave;

    /// <summary>When the last present member went, while nobody is present.</summary>
    public TimeSpan? EmptySince;

    /// <summary>The library map and version it was opened from, if any.</summary>
    public string? SourceMap;
    public string? SourceVersion;

    /// <summary>Entities across the session's maps, as last counted, plus placements since.</summary>
    public int EntityCount;
    public TimeSpan CountedAt;

    /// <summary>The limits last sent to its members.</summary>
    public WFMappingLimitsState? SentLimits;

    /// <summary>The PNG template shared with every member, if any.</summary>
    public WFMappingSessionTemplate? Template;

    /// <summary>The live test running on the session, if any.</summary>
    public Test.WFMappingTest? Test;

    /// <summary>Game time before which no new live test may start, after the last one ended.</summary>
    public TimeSpan TestCooldownUntil;
}

/// <summary>Raised when a player stops being a member of a session: they left, were kicked or it closed.</summary>
public sealed class WFMappingMembershipEndedEvent(NetUserId user, int sessionId) : EntityEventArgs
{
    public readonly NetUserId User = user;
    public readonly int SessionId = sessionId;
}
