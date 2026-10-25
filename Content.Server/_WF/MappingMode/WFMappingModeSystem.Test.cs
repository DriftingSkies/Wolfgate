using System.Linq;
using Content.Server._WF.MappingMode.Test;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.MappingMode;

/// <summary>The hooks live testing needs from the mode: the test flag on maps, and who is in a test.</summary>
public sealed partial class WFMappingModeSystem
{
    [Dependency] private WFMappingTestSystem _test = default!;

    /// <summary>Whether a player is walking their session's live test, which refuses them every edit.</summary>
    public bool IsTesting(ICommonSession player)
    {
        return _test.IsTesting(player.UserId);
    }

    /// <summary>Tags a map as a running level of a session's live test.</summary>
    public void RegisterTestMap(EntityUid map, int sessionId)
    {
        RegisterMap(map, WFMappingMapKind.Test, sessionId);
    }

    /// <summary>Whether a map is a level of the session's live test.</summary>
    public bool IsTestMap(EntityUid map, WFMappingSession session)
    {
        return _registry.TryGetValue(map, out var entry) &&
               entry.Kind == WFMappingMapKind.Test &&
               entry.SessionId == session.Id;
    }

    /// <summary>Whether a map is a level of any session's live test.</summary>
    public bool IsTestMap(EntityUid map)
    {
        return TestSessionOf(map) != null;
    }

    /// <summary>The id of the session whose live test a map is a level of, or null.</summary>
    public int? TestSessionOf(EntityUid map)
    {
        return _registry.TryGetValue(map, out var entry) && entry.Kind == WFMappingMapKind.Test ? entry.SessionId : null;
    }

    /// <summary>The live test levels of a session still registered.</summary>
    public List<EntityUid> TestMaps(int sessionId)
    {
        return _registry
            .Where(pair => pair.Value.Kind == WFMappingMapKind.Test && pair.Value.SessionId == sessionId)
            .Select(pair => pair.Key)
            .ToList();
    }

    /// <summary>
    /// A tester's observer is gone for good (they disconnected in the test): they count as absent, as a detached
    /// observer does.
    /// </summary>
    public void ObserverGone(NetUserId user, EntityUid observer)
    {
        MarkAbsent(user, observer);
    }
}
