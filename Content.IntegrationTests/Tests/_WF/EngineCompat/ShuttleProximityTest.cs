using System.Numerics;
using Content.Server.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;

namespace Content.IntegrationTests.Tests._WF.EngineCompat;

/// <summary>Checks randomized FTL placement preserves valid bounds on small grids.</summary>
[TestFixture]
public sealed class ShuttleProximityTest
{
    /// <summary>Places small shuttles beside the target without inverted bounds or overlap.</summary>
    [Test]
    public async Task SmallGridProximityHandlesRandomMargins()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var transforms = entities.System<SharedTransformSystem>();
            var shuttles = entities.System<ShuttleSystem>();
            var random = server.ResolveDependency<IRobustRandom>();
            var shuttle = maps.CreateGridEntity(map.MapId);
            maps.SetTile(shuttle, Vector2i.Zero, map.Tile.Tile);
            var shuttleTransform = entities.GetComponent<TransformComponent>(shuttle);

            for (var seed = 0; seed < 32; seed++)
            {
                transforms.SetCoordinates(shuttle.Owner, shuttleTransform, new EntityCoordinates(map.MapUid, new Vector2(100f, 100f)), rotation: Angle.Zero);
                random.SetSeed(seed);
                Assert.That(shuttles.TryFTLProximity(shuttle, map.Grid.Owner), Is.True, $"FTL placement failed for seed {seed}.");
                var shuttleBounds = transforms.GetWorldMatrix(shuttle).TransformBox(shuttle.Comp.LocalAABB);
                var targetBounds = transforms.GetWorldMatrix(map.Grid.Owner).TransformBox(map.Grid.Comp.LocalAABB);
                Assert.That(shuttleBounds.Intersects(targetBounds), Is.False, $"FTL placement overlapped the target for seed {seed}.");
            }

            entities.DeleteEntity(shuttle);
        });

        await pair.CleanReturnAsync();
    }
}
