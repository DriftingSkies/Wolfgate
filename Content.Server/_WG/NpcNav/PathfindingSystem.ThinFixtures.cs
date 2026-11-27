using System.Numerics;
using Robust.Shared.Physics.Dynamics;

namespace Content.Server.NPC.Pathfinding;

public sealed partial class PathfindingSystem
{
    /// <summary>Slack so a fixture that only touches a sample cell's edge doesn't block that cell.</summary>
    private const float WgThinCellSlack = 0.001f;

    /// <summary>
    /// Whether a fixture thinner than a sample cell on either axis overlaps the cell around a navmesh sample point.
    /// Such a fixture (a railing, fence or barricade) can sit between sample points and never hit one.
    /// </summary>
    private static bool WgThinFixtureBlocks(Fixture fixture, Vector2 localPos)
    {
        const float cell = 1f / SubStep;
        const float half = cell / 2f - WgThinCellSlack;

        foreach (var proxy in fixture.Proxies)
        {
            var aabb = proxy.AABB;
            if (aabb.Width >= cell && aabb.Height >= cell)
                continue;

            if (aabb.Left < localPos.X + half && aabb.Right > localPos.X - half &&
                aabb.Bottom < localPos.Y + half && aabb.Top > localPos.Y - half)
                return true;
        }

        return false;
    }
}
