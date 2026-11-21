using System.Collections.Generic;
using Content.Server._WG.EntityCensus;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.EntityCensus;

/// <summary>Checks the census files entities by place and prototype and reports what came and went between counts.</summary>
[TestFixture]
[TestOf(typeof(WGEntityCensusSystem))]
public sealed class EntityCensusTest
{
    private const string Prototype = "CrowbarRed";

    [Test]
    public async Task CountsPlacesPrototypesAndFlow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var meta = server.System<MetaDataSystem>();
        var census = server.System<WGEntityCensusSystem>();
        var map = await pair.CreateTestMap();

        var spawned = new List<EntityUid>();
        EntityUid grid = default;
        await server.WaitAssertion(() =>
        {
            var before = census.TakeNow();

            var made = maps.CreateGridEntity(map.MapId);
            grid = made.Owner;
            maps.SetTile(grid, made.Comp, Vector2i.Zero, map.Tile.Tile);
            for (var i = 0; i < 6; i++)
            {
                spawned.Add(entMan.SpawnEntity(Prototype, new EntityCoordinates(grid, 0.5f, 0.5f)));
            }

            meta.SetEntityPaused(spawned[0], true);
            var loose = entMan.SpawnEntity(Prototype, new MapCoordinates(500f, 500f, map.MapId));
            spawned.Add(loose);

            var after = census.TakeNow();
            Assert.Multiple(() =>
            {
                Assert.That(after.Total, Is.EqualTo(entMan.EntityCount));
                Assert.That(after.Prototypes.GetValueOrDefault(Prototype) - before.Prototypes.GetValueOrDefault(Prototype),
                    Is.EqualTo(7));
                Assert.That(after.Kinds[(int) WGCensusKind.OtherGrid] - before.Kinds[(int) WGCensusKind.OtherGrid],
                    Is.EqualTo(7), "The grid and the six on it.");
                Assert.That(after.Paused[(int) WGCensusKind.OtherGrid] - before.Paused[(int) WGCensusKind.OtherGrid],
                    Is.EqualTo(1));
                Assert.That(after.Kinds[(int) WGCensusKind.Space] - before.Kinds[(int) WGCensusKind.Space],
                    Is.EqualTo(1));
                Assert.That(after.GridCounts[grid], Is.EqualTo(7));
                Assert.That(after.Spawned.GetValueOrDefault(Prototype), Is.EqualTo(7));
            });
        });

        await server.WaitAssertion(() =>
        {
            entMan.DeleteEntity(spawned[1]);
            entMan.DeleteEntity(spawned[2]);
            entMan.SpawnEntity(Prototype, new EntityCoordinates(grid, 0.5f, 0.5f));
            // One that comes and goes between two counts still counts both ways.
            entMan.DeleteEntity(entMan.SpawnEntity(Prototype, new EntityCoordinates(grid, 0.5f, 0.5f)));

            var flow = census.TakeNow();
            Assert.Multiple(() =>
            {
                Assert.That(flow.Deleted.GetValueOrDefault(Prototype), Is.EqualTo(3));
                Assert.That(flow.Spawned.GetValueOrDefault(Prototype), Is.EqualTo(2));
            });

            entMan.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }
}
