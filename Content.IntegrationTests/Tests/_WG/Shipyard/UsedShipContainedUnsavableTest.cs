#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Shipyard;
using Content.Shared.Mech.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Shipyard;

/// <summary>A sold ship's containers must come back whole when they hold things that aren't normally saved.</summary>
[TestFixture]
public sealed class UsedShipContainedUnsavableTest
{
    private const string Crate = "CrateCybersunDarkGygaxBundle";
    private const string Mech = "MechGygaxSyndieFilled";

    [Test]
    public async Task MechInACrateComesAlong()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var grid = CreateGrid(entMan, map);
            entMan.SpawnEntity(Crate, new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            var equipment = CountEquipment(entMan, MechsAboard(entMan, grid));

            var loaded = CopyAsSold(entMan, grid);
            var mechs = MechsAboard(entMan, loaded);
            Assert.That(mechs, Has.Count.EqualTo(1));
            Assert.That(entMan.System<SharedContainerSystem>().IsEntityInContainer(mechs[0]), "The mech should still be in its crate.");
            Assert.That(CountEquipment(entMan, mechs), Is.EqualTo(equipment));
            entMan.DeleteEntity(loaded);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MechInsideACarriedMechComesAlong()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var containers = entMan.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = CreateGrid(entMan, map);
            var coords = new EntityCoordinates(grid, new Vector2(0.5f, 0.5f));
            var mech = entMan.SpawnEntity(Mech, coords);
            var container = containers.EnsureContainer<Container>(mech, "wg_test");
            var inner = entMan.SpawnEntity(Mech, coords);
            Assert.That(containers.Insert(inner, container));
            // Both share a prototype, so the copy alone wouldn't show the inner mech was found.
            Assert.That(entMan.System<UsedShipMarketSystem>().GetUnsavableAboard(grid), Does.Contain(inner));
            var equipment = CountEquipment(entMan, MechsAboard(entMan, grid));

            var loaded = CopyAsSold(entMan, grid);
            var mechs = MechsAboard(entMan, loaded);
            Assert.That(mechs, Has.Count.EqualTo(2));
            Assert.That(mechs.Count(m => containers.IsEntityInContainer(m)), Is.EqualTo(1), "The inner mech should still be inside the outer one.");
            Assert.That(CountEquipment(entMan, mechs), Is.EqualTo(equipment));
            entMan.DeleteEntity(loaded);
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid CreateGrid(IEntityManager entMan, Robust.UnitTesting.Pool.TestMapData map)
    {
        var maps = entMan.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(map.MapId);
        maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
        return grid.Owner;
    }

    /// <summary>Copies a grid the way a sale does, then loads the copy.</summary>
    private static EntityUid CopyAsSold(IEntityManager entMan, EntityUid grid)
    {
        var shipyard = entMan.System<ShipyardSystem>();
        var carry = entMan.System<UsedShipMarketSystem>().GetUnsavableAboard(grid);
        Assert.That(shipyard.TrySaveShip(grid, carry, out var data), Is.True);
        Assert.That(shipyard.TryAddSavedShip(data!, out var loaded), Is.True);
        return loaded!.Value;
    }

    private static int CountEquipment(IEntityManager entMan, List<EntityUid> mechs)
    {
        return mechs.Sum(m => entMan.GetComponent<MechComponent>(m).EquipmentContainer.ContainedEntities.Count);
    }

    private static List<EntityUid> MechsAboard(IEntityManager entMan, EntityUid grid)
    {
        var mechs = new List<EntityUid>();
        var pending = new Queue<EntityUid>();
        pending.Enqueue(grid);
        while (pending.TryDequeue(out var uid))
        {
            var children = entMan.GetComponent<TransformComponent>(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                pending.Enqueue(child);
                if (entMan.GetComponent<MetaDataComponent>(child).EntityPrototype?.ID == Mech)
                    mechs.Add(child);
            }
        }

        return mechs;
    }
}
