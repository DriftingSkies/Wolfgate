using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Mind;
using Content.Shared.Station.Components;
using System.Linq;
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
    /// <summary>
    /// A bought ship, which is also its own station, is counted as a player ship, and an entity made without a
    /// prototype is named by its components.
    /// </summary>
    [Test]
    public async Task OwnedShipsAndPrototypelessEntitiesAreNamed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var census = server.System<WGEntityCensusSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var before = census.TakeNow();

            var ship = maps.CreateGridEntity(map.MapId);
            maps.SetTile(ship.Owner, ship.Comp, Vector2i.Zero, map.Tile.Tile);
            entMan.AddComponent<ShuttleDeedComponent>(ship.Owner);
            entMan.AddComponent<StationMemberComponent>(ship.Owner);
            entMan.SpawnEntity(Prototype, new EntityCoordinates(ship.Owner, 0.5f, 0.5f));

            var station = maps.CreateGridEntity(map.MapId);
            maps.SetTile(station.Owner, station.Comp, Vector2i.Zero, map.Tile.Tile);
            entMan.AddComponent<StationMemberComponent>(station.Owner);
            entMan.SpawnEntity(Prototype, new EntityCoordinates(station.Owner, 0.5f, 0.5f));

            var after = census.TakeNow();
            Assert.Multiple(() =>
            {
                Assert.That(after.Kinds[(int) WGCensusKind.PlayerShip] - before.Kinds[(int) WGCensusKind.PlayerShip],
                    Is.EqualTo(2), "The bought ship and what is on it.");
                Assert.That(after.Kinds[(int) WGCensusKind.Station] - before.Kinds[(int) WGCensusKind.Station],
                    Is.EqualTo(2), "The station and what is on it.");
                Assert.That(after.Prototypes.Keys.Any(k => k.StartsWith("(none: ") && k.Contains("MapGrid")), Is.True,
                    $"A grid has no prototype and should be named for what it is: {string.Join(", ", after.Prototypes.Keys.Where(k => k.StartsWith("(none")))}");
                Assert.That(after.Spawned.Keys.Any(k => k.StartsWith("(none: ") && k.Contains("MapGrid")), Is.True,
                    $"A grid made since the last count is not named in what spawned: {string.Join(", ", after.Spawned.Keys)}");
            });

            entMan.DeleteEntity(ship.Owner);
            entMan.DeleteEntity(station.Owner);
            var gone = census.TakeNow();
            Assert.That(gone.Deleted.Keys.Any(k => k.StartsWith("(none: ") && k.Contains("MapGrid")), Is.True,
                "A deleted grid was counted under a bare (none).");

            // One that becomes what it is after it is made is named the same way in both tables of one count.
            var mind = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<MindComponent>(mind);
            var named = census.TakeNow();
            Assert.Multiple(() =>
            {
                Assert.That(named.Prototypes.GetValueOrDefault("(none: Mind)") - gone.Prototypes.GetValueOrDefault("(none: Mind)"),
                    Is.EqualTo(1), "The live table does not name it for what it became.");
                Assert.That(named.Spawned.GetValueOrDefault("(none: Mind)"), Is.EqualTo(1), "The spawn table names it otherwise.");
            });

            // Made and deleted between counts: counted once each way, under one name.
            var brief = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<MindComponent>(brief);
            entMan.DeleteEntity(brief);
            var flow = census.TakeNow();
            Assert.That(flow.Spawned.Where(p => p.Key.StartsWith("(none")).Sum(p => p.Value), Is.EqualTo(1));
            Assert.That(flow.Deleted.Where(p => p.Key.StartsWith("(none")).Sum(p => p.Value), Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }
}
