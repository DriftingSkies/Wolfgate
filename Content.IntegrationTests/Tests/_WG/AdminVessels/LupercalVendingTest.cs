#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.ShipCommand;
using Content.Server.Power.Components;
using Content.Server.VendingMachines;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Power.EntitySystems;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.AdminVessels;

/// <summary>
/// The Lupercal's vending machines, fitted as its vessel prototype fits them: every line unlimited, its extra lines
/// stocked, a vend never drawing a line down, and a machine brought aboard after the fitting pass stocked too.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipFittingSystem))]
public sealed class LupercalVendingTest
{
    /// <summary>Lupercal machines whose packs carry extra lines; the last is brought aboard after the fitting pass.</summary>
    private static readonly string[] Machines =
    {
        "VendingMachineCoffee",
        "VendingMachineMedical",
        "VendingMachineSnack",
        "VendingMachineCola",
    };

    [Test]
    public async Task LupercalVendorsNeverRunOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var fitting = entMan.System<WGShipFittingSystem>();
        var vending = entMan.System<VendingMachineSystem>();
        var mapSys = entMan.System<SharedMapSystem>();
        var ticks = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        var vessel = protos.Index<VesselPrototype>("WGLupercal");
        Assert.That(vessel.AddComponents.TryGetValue("WGShipFitting", out var entry), Is.True, "The Lupercal isn't fitted.");
        var lupercal = (WGShipFittingComponent) entry!.Component;
        Assert.That(lupercal.UnlimitedVending, Is.True, "The Lupercal's vending machines can run out.");

        var vendors = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            if (!mapSys.IsInitialized(map.MapId))
                mapSys.InitializeMap(map.MapId);

            foreach (var id in Machines.SkipLast(1))
                vendors.Add(Spawn(server, id, map.GridCoords));

            var comp = entMan.AddComponent<WGShipFittingComponent>(grid);
            comp.UnlimitedVending = lupercal.UnlimitedVending;
            comp.VendingExtras = new(lupercal.VendingExtras);
            comp.Fitted = false;
            fitting.Fit((grid, comp));

            vendors.Add(Spawn(server, Machines[^1], map.GridCoords));
        });

        // The five-second sweep stocks the machine brought aboard after the pass.
        await pair.RunTicksSync(ticks * 6);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var uid in vendors)
                {
                    var comp = entMan.GetComponent<VendingMachineComponent>(uid);
                    var name = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID;
                    Assert.That(comp.Inventory.Values.Where(e => e.Amount != uint.MaxValue).Select(e => e.ID), Is.Empty,
                        $"{name} has limited lines.");

                    var hasExtras = lupercal.VendingExtras.TryGetValue(comp.PackPrototypeId, out var extras);
                    Assert.That(hasExtras, Is.True, $"{name}'s pack has no Lupercal extras.");
                    if (!hasExtras)
                        continue;

                    var missing = protos.Index(extras).StartingInventory.Keys.Where(k => !comp.Inventory.ContainsKey(k));
                    Assert.That(missing, Is.Empty, $"{name} lacks its Lupercal extras.");
                }
            });
        });

        // Vend the same line again and again: it never draws down and the items come out.
        var coffee = vendors[0];
        var line = entMan.GetComponent<VendingMachineComponent>(coffee).Inventory.Keys.First();
        const int vends = 3;
        for (var i = 0; i < vends; i++)
        {
            await server.WaitAssertion(() =>
            {
                Assert.That(vending.TryEjectVendorItem(coffee, InventoryType.Regular, line, false), Is.True,
                    $"Vend {i + 1} of {line} was refused.");
                Assert.That(entMan.GetComponent<VendingMachineComponent>(coffee).Inventory[line].Amount, Is.EqualTo(uint.MaxValue),
                    $"Vend {i + 1} drew {line} down.");
            });

            // Until the item is out and the machine takes the next order: each machine has its own eject delay (a coffee
            // machine's brew is longer than most).
            var ejecting = true;
            for (var wait = 0; wait < 15 && ejecting; wait++)
            {
                await pair.RunTicksSync(ticks);
                await server.WaitPost(() => ejecting = entMan.GetComponent<VendingMachineComponent>(coffee).Ejecting);
            }
        }

        // A forced eject (a hit, an EMP) draws a line down; the sweep puts it back.
        await server.WaitPost(() => vending.EjectRandom(coffee, throwItem: false, forceEject: true));
        await pair.RunTicksSync(ticks * 6);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<VendingMachineComponent>(coffee);
            Assert.That(comp.Inventory.Values.Where(e => e.Amount != uint.MaxValue).Select(e => e.ID), Is.Empty,
                "A line knocked loose stayed drawn down.");

            var vended = 0;
            var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var meta, out var xform))
            {
                if (xform.MapID == map.MapId && meta.EntityPrototype?.ID == line)
                    vended++;
            }

            Assert.That(vended, Is.GreaterThanOrEqualTo(vends), $"Only {vended} of {vends} vends of {line} came out.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns a vending machine that runs without power.</summary>
    private static EntityUid Spawn(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, string id, EntityCoordinates coords)
    {
        var uid = server.EntMan.SpawnEntity(id, coords);
        server.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
        server.EntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
        return uid;
    }
}
