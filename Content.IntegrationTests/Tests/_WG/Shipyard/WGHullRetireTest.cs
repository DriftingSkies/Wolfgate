#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Shipyard;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.Shipyard;

/// <summary>A sold hull is parked, copied and deleted over many ticks, and the copy has to come back whole.</summary>
[TestFixture]
[TestOf(typeof(WGHullRetireSystem))]
public sealed class WGHullRetireTest
{
    private const string Crate = "CrateGenericSteel";

    [Test]
    public async Task CopyLoadsUnpausedAndHullGoes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var retire = entMan.System<WGHullRetireSystem>();
        var map = await pair.CreateTestMap();

        EntityUid hull = default;
        Task<string?> copy = default!;
        await server.WaitAssertion(() =>
        {
            hull = CreateHull(entMan, map.Tile.Tile, 300);
            copy = retire.RetireWithCopy(hull, new());
            Assert.That(entMan.GetComponent<MetaDataComponent>(hull).EntityPaused, "The hull should wait on a paused map.");
        });

        await RunUntil(pair, () => entMan.Deleted(hull) && copy.IsCompleted);
        Assert.That(copy.GetAwaiter().GetResult(), Is.Not.Null, "The copy failed.");

        await server.WaitAssertion(() =>
        {
            Assert.That(retire.Last.CopyTicks + retire.Last.DeleteTicks, Is.GreaterThan(1), "The hull went in one tick.");

            var shipyard = entMan.System<ShipyardSystem>();
            Assert.That(shipyard.TryAddSavedShip(copy.GetAwaiter().GetResult()!, out var loaded), Is.True);
            var crates = Crates(entMan, loaded!.Value);
            Assert.That(crates, Has.Count.EqualTo(300));
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<MetaDataComponent>(loaded.Value).EntityPaused, Is.False, "The copy loaded paused.");
                Assert.That(crates.Any(c => entMan.GetComponent<MetaDataComponent>(c).EntityPaused), Is.False, "A crate on the copy loaded paused.");
            });
            entMan.DeleteEntity(loaded.Value);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EntityGoneMidCopyStillCopies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var retire = entMan.System<WGHullRetireSystem>();
        var map = await pair.CreateTestMap();

        EntityUid hull = default;
        Task<string?> copy = default!;
        await server.WaitPost(() =>
        {
            hull = CreateHull(entMan, map.Tile.Tile, 400);
            copy = retire.RetireWithCopy(hull, new());
        });

        await server.WaitRunTicks(1);
        await server.WaitPost(() =>
        {
            if (!entMan.Deleted(hull))
                entMan.DeleteEntity(Crates(entMan, hull).First());
        });

        await RunUntil(pair, () => entMan.Deleted(hull) && copy.IsCompleted);
        Assert.That(copy.GetAwaiter().GetResult(), Is.Not.Null, "The copy should start over without the deleted entity.");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HullDeletedMeanwhileEndsTheCopy()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        Task<string?> copy = default!;
        await server.WaitPost(() =>
        {
            var hull = CreateHull(entMan, map.Tile.Tile, 1);
            copy = entMan.System<WGHullRetireSystem>().RetireWithCopy(hull, new());
            entMan.DeleteEntity(hull);
        });

        await RunUntil(pair, () => copy.IsCompleted);
        Assert.That(copy.GetAwaiter().GetResult(), Is.Null);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GhostStaysBehind()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var players = server.ResolveDependency<ISharedPlayerManager>();
        var map = await pair.CreateTestMap();

        EntityUid hull = default;
        EntityUid ghost = default;
        await server.WaitPost(() =>
        {
            hull = CreateHull(entMan, map.Tile.Tile, 1);
            var xforms = entMan.System<SharedTransformSystem>();
            xforms.SetMapCoordinates(hull, new MapCoordinates(new Vector2(3000f, 0f), map.MapId));

            ghost = entMan.SpawnEntity("MobObserver", new EntityCoordinates(hull, new Vector2(0.5f, 0.5f)));
            players.SetAttachedEntity(players.Sessions.First(), ghost);
            entMan.System<WGHullRetireSystem>().Retire(hull);
        });

        await RunUntil(pair, () => entMan.Deleted(hull));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(ghost), "The ghost was deleted with the hull.");
            var xform = entMan.GetComponent<TransformComponent>(ghost);
            Assert.That(xform.MapID, Is.EqualTo(map.MapId), "The ghost should stay on its map.");
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid CreateHull(IEntityManager entMan, Tile tile, int crates)
    {
        var maps = entMan.System<SharedMapSystem>();
        maps.CreateMap(out var mapId);
        var grid = maps.CreateGridEntity(mapId);
        maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, tile);
        var coords = new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
        for (var i = 0; i < crates; i++)
        {
            entMan.SpawnEntity(Crate, coords);
        }

        return grid.Owner;
    }

    private static List<EntityUid> Crates(IEntityManager entMan, EntityUid grid)
    {
        var crates = new List<EntityUid>();
        var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == Crate)
                crates.Add(child);
        }

        return crates;
    }

    private static async Task RunUntil(Pair.TestPair pair, Func<bool> done)
    {
        for (var i = 0; i < 1800 && !done(); i++)
        {
            await pair.Server.WaitRunTicks(1);
        }

        Assert.That(done(), "It didn't finish in a minute of game time.");
    }
}
