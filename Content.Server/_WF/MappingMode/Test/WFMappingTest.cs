using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server._WF.MappingMode.Test;

/// <summary>
/// A session's running live test: an initialised copy of its levels with the ship made flyable and a range to fly to,
/// and the members walking it as their characters.
/// </summary>
public sealed class WFMappingTest(List<EntityUid> maps, EntityUid ground, EntityUid? ship, EntityCoordinates spawn,
    TimeSpan started, TimeSpan ends)
{
    /// <summary>The test's levels, bottom-up; one z-network of their own.</summary>
    public readonly List<EntityUid> Maps = maps;

    /// <summary>The copy of the session's ground level, where the ship and the range are.</summary>
    public readonly EntityUid Ground = ground;

    /// <summary>The largest grid of the ground copy, given what a bought ship has; null on an empty level.</summary>
    public readonly EntityUid? Ship = ship;

    /// <summary>Where every tester lands.</summary>
    public readonly EntityCoordinates Spawn = spawn;

    public readonly TimeSpan Started = started;

    /// <summary>Game time the test ends for everyone.</summary>
    public readonly TimeSpan Ends = ends;

    /// <summary>The members in the test right now.</summary>
    public readonly Dictionary<NetUserId, WFMappingTester> Testers = new();
}

/// <summary>A member in a live test: their character, and the parked observer they go back to.</summary>
public sealed class WFMappingTester(EntityUid mob, EntityUid observer, MapCoordinates observerAt)
{
    public readonly EntityUid Mob = mob;
    public readonly EntityUid Observer = observer;

    /// <summary>Where the observer was when the test started, so returning is a re-attach there.</summary>
    public readonly MapCoordinates ObserverAt = observerAt;

    /// <summary>Game time they are sent back on their own, set when their character dies.</summary>
    public TimeSpan? ReturnAt;
}
