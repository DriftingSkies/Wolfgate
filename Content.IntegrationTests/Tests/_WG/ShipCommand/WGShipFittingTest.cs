#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Remade.Gifts;
using Content.Server._WG.ShipCommand;
using Content.Server.Cargo.Systems;
using Content.Server.Construction.Components;
using Content.Server.Lathe;
using Content.Server.Power.Components;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>
/// Fitting a commanded ship out: every machine gets the best parts, ordinary lathes join the ship's ore silo while
/// bottomless ones stay off it, and a bottomless lathe keeps its materials topped up, for printing only.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipFittingSystem))]
public sealed class WGShipFittingTest
{
    [Test]
    public async Task FittingUpgradesLinksAndTopsUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var fitting = entMan.System<WGShipFittingSystem>();
        var materials = entMan.System<SharedMaterialStorageSystem>();
        var mapSys = entMan.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid lathe = default, silo = default, bottomless = default;
        await server.WaitPost(() =>
        {
            // Machines build their stock parts on map init.
            if (!mapSys.IsInitialized(map.MapId))
                mapSys.InitializeMap(map.MapId);
            lathe = entMan.SpawnEntity("Protolathe", map.GridCoords);
            silo = entMan.SpawnEntity("MachineMaterialSilo", map.GridCoords);
            bottomless = entMan.SpawnEntity("Autolathe", map.GridCoords);
            entMan.EnsureComponent<WGBottomlessMaterialsComponent>(bottomless).Amount = 1000;
        });

        await server.WaitAssertion(() =>
        {
            var parts = entMan.GetComponent<MachineComponent>(lathe).PartContainer.ContainedEntities
                .Where(entMan.HasComponent<MachinePartComponent>).ToList();
            Assert.That(parts, Is.Not.Empty, "Precondition: the lathe built its stock parts.");
            var before = parts.Sum(p => entMan.GetComponent<MachinePartComponent>(p).Rating);

            var comp = entMan.AddComponent<WGShipFittingComponent>(grid);
            comp.Fitted = false;
            fitting.Fit((grid, comp));

            parts = entMan.GetComponent<MachineComponent>(lathe).PartContainer.ContainedEntities
                .Where(entMan.HasComponent<MachinePartComponent>).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(parts.Sum(p => entMan.GetComponent<MachinePartComponent>(p).Rating), Is.GreaterThan(before),
                    "The machine's parts were upgraded.");
                Assert.That(fitting.Fit((grid, comp)), Is.EqualTo(0), "A second pass finds nothing left to upgrade.");
                Assert.That(entMan.GetComponent<OreSiloClientComponent>(lathe).Silo, Is.EqualTo(silo), "An ordinary lathe joins the ship's silo.");
                Assert.That(entMan.GetComponent<OreSiloClientComponent>(bottomless).Silo, Is.Null, "A bottomless lathe stays off the silo.");
            });
        });

        await pair.RunTicksSync(240);
        await server.WaitAssertion(() =>
        {
            Assert.That(materials.GetMaterialAmount(bottomless, "Steel"), Is.EqualTo(1000), "A bottomless lathe is topped up.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A bottomless lathe's stock is for printing only: it won't eject, adds nothing to the lathe's price, drops nothing
    /// when the lathe is taken apart, and what it prints sells for nothing.
    /// </summary>
    [Test]
    public async Task BottomlessStockStaysInTheLathe()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var materials = entMan.System<SharedMaterialStorageSystem>();
        var pricing = entMan.System<PricingSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var bottomless = entMan.SpawnEntity("Autolathe", map.GridCoords);
            var plain = entMan.SpawnEntity("Autolathe", map.GridCoords);
            foreach (var lathe in new[] { bottomless, plain })
            {
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(lathe, false);
                entMan.GetComponent<ApcPowerReceiverComponent>(lathe).Powered = true;
            }

            entMan.EnsureComponent<WGBottomlessMaterialsComponent>(bottomless);
            var priceBefore = pricing.GetPrice(bottomless);
            Assert.That(materials.TryChangeMaterialAmount(bottomless, "Steel", 5000), Is.True, "Precondition: the lathe takes steel.");
            Assert.That(materials.TryChangeMaterialAmount(plain, "Steel", 5000), Is.True, "Precondition: the lathe takes steel.");

            var eject = new WGMatterEjectAttemptEvent(bottomless, bottomless, "Steel", 100);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref eject);
            var plainEject = new WGMatterEjectAttemptEvent(plain, plain, "Steel", 100);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref plainEject);
            Assert.Multiple(() =>
            {
                Assert.That(eject.Cancelled, Is.True, "A bottomless lathe's stock came out as sheets.");
                Assert.That(plainEject.Cancelled, Is.False, "An ordinary lathe's stock was refused.");
                Assert.That(pricing.GetPrice(bottomless), Is.EqualTo(priceBefore).Within(0.01), "The bottomless stock added to the lathe's price.");
            });

            var control = Print(server, plain, null);
            Assert.That(pricing.GetPrice(control), Is.GreaterThan(0d), "Precondition: an ordinary print sells.");
            var made = Print(server, bottomless, control);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<WGBottomlessPrintComponent>(made), Is.True, "A bottomless print wasn't marked.");
                Assert.That(pricing.GetPrice(made), Is.Zero, "A bottomless print still sells.");
            });

            var sheets = SheetsOn(entMan, map.MapId);
            entMan.EventBus.RaiseLocalEvent(bottomless, new MachineDeconstructedEvent());
            Assert.Multiple(() =>
            {
                Assert.That(materials.GetMaterialAmount(bottomless, "Steel", localOnly: true), Is.Zero, "The stock wasn't emptied.");
                Assert.That(SheetsOn(entMan, map.MapId), Is.EqualTo(sheets), "Taking the lathe apart spilled its stock as sheets.");
            });

            entMan.DeleteEntity(made);
            entMan.DeleteEntity(control);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Prints a green crowbar on a powered lathe from start to finish and returns it.</summary>
    private static EntityUid Print(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid lathe, EntityUid? except)
    {
        var lathes = server.System<LatheSystem>();
        var recipe = server.ProtoMan.Index<LatheRecipePrototype>("CrowbarGreen");
        var comp = server.EntMan.GetComponent<Content.Shared.Lathe.LatheComponent>(lathe);
        Assert.That(lathes.TryAddToQueue(lathe, recipe, 1, comp), Is.True, "The lathe wouldn't queue the recipe.");
        Assert.That(lathes.TryStartProducing(lathe, comp), Is.True, "The lathe wouldn't start printing.");
        lathes.FinishProducing(lathe);

        var mapId = server.EntMan.GetComponent<TransformComponent>(lathe).MapID;
        foreach (var ent in server.EntMan.AllEntities<MetaDataComponent>())
        {
            if (ent.Owner != except && ent.Comp.EntityPrototype?.ID == "CrowbarGreen"
                && server.EntMan.GetComponent<TransformComponent>(ent.Owner).MapID == mapId)
                return ent.Owner;
        }

        Assert.Fail("The lathe printed no crowbar.");
        return default;
    }

    /// <summary>How many material stacks lie on a map.</summary>
    private static int SheetsOn(IEntityManager entMan, MapId map)
    {
        var count = 0;
        var query = entMan.EntityQueryEnumerator<MaterialComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID == map)
                count++;
        }

        return count;
    }
}
