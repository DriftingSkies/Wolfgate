#nullable enable
using Content.Server.NPC.Pathfinding;
using Content.Shared.Physics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.NpcNav;

/// <summary>Railings and fences block the navmesh cells they sit in, and leave the rest of their tile open.</summary>
[TestFixture]
[TestOf(typeof(PathfindingSystem))]
public sealed class ThinObstacleNavmeshTest
{
    [Test]
    public async Task ThinFixturesBlockTheirCells()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitPost(() =>
        {
            var em = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var xforms = server.System<SharedTransformSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = 0; x < 24; x++)
            for (var y = 0; y < 8; y++)
                maps.SetTile(grid, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            em.SpawnEntity("Railing", new EntityCoordinates(grid, 4.5f, 4.5f));
            em.SpawnEntity("FenceMetalStraight", new EntityCoordinates(grid, 8.5f, 4.5f));
            var north = em.SpawnEntity("Railing", new EntityCoordinates(grid, 12.5f, 4.5f));
            xforms.SetLocalRotation(north, Angle.FromDegrees(180));
            em.SpawnEntity("WindowDirectional", new EntityCoordinates(grid, 16.5f, 4.5f));
        });
        await server.WaitRunTicks(30);

        var pathfinding = server.System<PathfindingSystem>();
        PathPoly? Poly(float x, float y) => pathfinding.GetPoly(new EntityCoordinates(grid, x, y));
        bool Blocked(float x, float y) => Poly(x, y) is { } poly && (poly.Data.CollisionLayer & (int) CollisionGroup.MobMask) != 0;

        await server.WaitAssertion(() =>
        {
            Assert.That(Blocked(4.5f, 4.1f), Is.True, "The railing's edge of its tile is open.");
            Assert.That(Blocked(4.5f, 4.6f), Is.False, "The railing blocks the open part of its tile.");

            Assert.That(Blocked(8.4f, 4.5f), Is.True, "The fence's west half-cell is open.");
            Assert.That(Blocked(8.6f, 4.5f), Is.True, "The fence's east half-cell is open.");
            Assert.That(Blocked(8.1f, 4.5f), Is.False, "The fence blocks its tile's west edge.");
            Assert.That(Blocked(8.9f, 4.5f), Is.False, "The fence blocks its tile's east edge.");

            Assert.That(Blocked(12.5f, 4.9f), Is.True, "The turned railing's edge of its tile is open.");
            var west = Poly(12.1f, 4.1f);
            var east = Poly(12.9f, 4.1f);
            Assert.That(west, Is.Not.Null);
            Assert.That(west!.Data.IsFreeSpace, Is.True, "Open floor beside a railing carries its climb flag.");
            Assert.That(west, Is.SameAs(east), "The railing split its tile's open floor into strips.");

            Assert.That(Blocked(16.5f, 4.1f), Is.True, "The directional window no longer blocks its edge.");
            Assert.That(Blocked(16.5f, 4.6f), Is.False, "The directional window blocks the open part of its tile.");
        });

        await pair.CleanReturnAsync();
    }
}
