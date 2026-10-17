#nullable enable
using System.Numerics;
using Content.Server._Crescent.Dispenser;
using Content.Server._Mono.VendingMachine;
using Content.Shared._Crescent.Dispenser;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.TradeGoods;

/// <summary>
/// A cargo chute refuses trade goods bought on its own grid and still pays for goods bought on another.
/// </summary>
[TestFixture]
[TestOf(typeof(DispenserSystem))]
public sealed class TradeChuteTest
{
    // Caelestinus Central maps this vendor next to this chute, and the chute pays far more than the vendor charges.
    private const string ChuteProto = "CargoChuteTradeMall";
    private const string VendorProto = "VendingMachineTraderDepot";
    private const string CrateProto = "TradeGoodFabric";

    [Test]
    public async Task ChuteRefusesLocalGoods()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mapMan = server.System<SharedMapSystem>();
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var interaction = entMan.System<SharedInteractionSystem>();
        var purchase = entMan.System<VendingMachinePurchaseSystem>();

        var chute = EntityUid.Invalid;
        var user = EntityUid.Invalid;
        var local = EntityUid.Invalid;
        var imported = EntityUid.Invalid;
        var payout = string.Empty;

        int CountPayouts()
        {
            var count = 0;
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == payout)
                    count++;
            }

            return count;
        }

        await server.WaitAssertion(() =>
        {
            var coords = map.GridCoords;
            chute = entMan.SpawnEntity(ChuteProto, coords);
            user = entMan.SpawnEntity(null, coords);

            var chuteComp = entMan.GetComponent<DispenserComponent>(chute);
            Assert.That(chuteComp.Inventory.TryGetValue(CrateProto, out var cash), Is.True,
                $"{ChuteProto} no longer takes {CrateProto}; pick a crate it does take.");
            payout = cash!;

            var vendor = entMan.SpawnEntity(VendorProto, coords);
            local = entMan.SpawnEntity(CrateProto, coords);
            purchase.MarkAsPurchased(local, vendor, 1000);

            var otherGrid = mapMan.CreateGridEntity(map.MapId);
            mapSys.SetTile(otherGrid.Owner, otherGrid.Comp, Vector2i.Zero, map.Tile.Tile);
            xformSys.SetLocalPosition(otherGrid.Owner, new Vector2(50f, 0f));

            var otherVendor = entMan.SpawnEntity(VendorProto, new EntityCoordinates(otherGrid.Owner, 0.5f, 0.5f));
            imported = entMan.SpawnEntity(CrateProto, coords);
            purchase.MarkAsPurchased(imported, otherVendor, 1000);
            Assert.That(purchase.WasPurchasedOnGrid(imported, otherGrid.Owner), Is.True,
                "The imported crate wasn't stamped with the other grid, so it can't test goods bought elsewhere.");

            Assert.That(interaction.InteractUsing(user, local, chute, coords, false, false), Is.True,
                "The chute should handle a crate it trades, even when refusing it.");
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(local), Is.False, "The chute took a crate bought on its own grid.");
            Assert.That(CountPayouts(), Is.Zero, "The chute paid for a crate bought on its own grid.");

            Assert.That(interaction.InteractUsing(user, imported, chute, map.GridCoords, false, false), Is.True);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(imported), Is.True, "The chute refused a crate bought on another grid.");
            Assert.That(CountPayouts(), Is.EqualTo(1), "The chute didn't pay for a crate bought on another grid.");
        });

        await pair.CleanReturnAsync();
    }
}
