using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.RoundSetup;

/// <summary>A crew member's condition as shown in public crew listings.</summary>
[Serializable, NetSerializable]
public enum WGCrewMemberStatus : byte { Waiting, Alive, Critical, Dead, Missing }

/// <summary>How much of a launched crew's ship is left.</summary>
[Serializable, NetSerializable]
public enum WGCrewShipCondition : byte { None, Intact, Damaged, Wrecked, Gone }

/// <summary>Pure rules for crew status summaries and join warnings.</summary>
public static class WGCrewStatusPolicy
{
    /// <summary>The share of the original deck above which a ship counts as intact.</summary>
    public const float IntactFraction = 0.9f;
    /// <summary>The share of the original deck below which a ship counts as wrecked.</summary>
    public const float WreckedFraction = 0.5f;

    /// <summary>Rates the largest surviving deck against the largest deck seen since launch.</summary>
    public static WGCrewShipCondition Condition(int tiles, int baseline)
    {
        if (tiles <= 0)
            return WGCrewShipCondition.Gone;
        if (baseline <= 0)
            return WGCrewShipCondition.Intact;
        var fraction = (float) tiles / baseline;
        return fraction >= IntactFraction ? WGCrewShipCondition.Intact
            : fraction >= WreckedFraction ? WGCrewShipCondition.Damaged
            : WGCrewShipCondition.Wrecked;
    }

    /// <summary>Spawned members who are alive or in critical condition.</summary>
    public static int Living(IEnumerable<WGCrewMemberStatus> statuses) =>
        statuses.Count(status => status is WGCrewMemberStatus.Alive or WGCrewMemberStatus.Critical);

    /// <summary>Members who have spawned into the round.</summary>
    public static int Deployed(IEnumerable<WGCrewMemberStatus> statuses) =>
        statuses.Count(status => status != WGCrewMemberStatus.Waiting);

    /// <summary>A started crew warrants a warning when every deployed member is down or its ship is lost.</summary>
    public static bool NeedsJoinWarning(bool started, IReadOnlyCollection<WGCrewMemberStatus> statuses, WGCrewShipCondition ship) =>
        started && (Deployed(statuses) > 0 && Living(statuses) == 0 ||
                    ship is WGCrewShipCondition.Wrecked or WGCrewShipCondition.Gone);
}
