#nullable enable
using Content.Server._EinsteinEngines.Power.Components;
using Content.Server._WG.Cyborgs;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.Silicons;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>
/// A cyborg can drain batteries into the cell in its slot, and carries a bank account.
/// </summary>
[TestFixture]
[TestOf(typeof(WGBorgPowerSystem))]
public sealed class BorgPowerTest
{
    private const string Borg = "BorgChassisMedical";
    private const string Cell = "PowerCellHigh";

    [Test]
    public async Task DrinksIntoSlottedCellTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var containers = server.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var borg = entMan.SpawnEntity(Borg, map.GridCoords);
            Assert.That(entMan.HasComponent<BatteryDrinkerComponent>(borg), "The cyborg cannot drain batteries.");
            Assert.That(entMan.HasComponent<BankAccountComponent>(borg), "The cyborg has no bank account.");

            var ev = new GetDrinkerBatteryEvent();
            entMan.EventBus.RaiseLocalEvent(borg, ref ev);
            Assert.That(ev.Battery, Is.Null, "A cyborg with no cell answered with a battery.");

            // Straight into the container: the chassis is locked, so the slot refuses an insert.
            var cell = entMan.SpawnEntity(Cell, map.GridCoords);
            Assert.That(containers.Insert(cell, containers.GetContainer(borg, "cell_slot"), force: true));

            ev = new GetDrinkerBatteryEvent();
            entMan.EventBus.RaiseLocalEvent(borg, ref ev);
            Assert.That(ev.Battery?.Owner, Is.EqualTo(cell), "The drained charge would not go to the slotted cell.");
        });

        await pair.CleanReturnAsync();
    }
}
