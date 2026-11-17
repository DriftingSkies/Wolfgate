#nullable enable
using Content.Server._WG.NetTraffic;
using Content.Server.Power.EntitySystems;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.Power.Components;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Silicons.Borgs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NetTraffic;

/// <summary>An entity nobody has open holds no UI state, and a cyborg only sends its window a charge change it would show.</summary>
[TestFixture]
[TestOf(typeof(WGUiStateTrimSystem))]
public sealed class WGUiStateTrimTest
{
    private const string Borg = "BorgChassisMedical";
    private const string Cell = "PowerCellHigh";
    private const string Canister = "AirCanister";
    private const string Viewer = "MobHuman";
    private const string Helm = "ComputerShuttle";

    [Test]
    public async Task BorgUiFollowsChargeOnlyWhileOpen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var containers = server.System<SharedContainerSystem>();
        var battery = server.System<BatterySystem>();
        var ui = server.System<UserInterfaceSystem>();
        EntityUid borg = default;
        EntityUid cell = default;
        EntityUid viewer = default;
        var step = 0f;

        await server.WaitAssertion(() =>
        {
            borg = entMan.SpawnEntity(Borg, map.GridCoords);
            cell = entMan.SpawnEntity(Cell, map.GridCoords);
            viewer = entMan.SpawnEntity(Viewer, map.GridCoords);
            // Straight into the container: the chassis is locked, so the slot refuses an insert.
            Assert.That(containers.Insert(cell, containers.GetContainer(borg, "cell_slot"), force: true));
            step = entMan.GetComponent<BatteryComponent>(cell).MaxCharge * 0.05f;
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<NavBoundUserInterfaceState>(borg, RadarConsoleUiKey.Key, out _), Is.False,
                "A cyborg nobody is using as a radar holds a nav state.");
            ui.OpenUi(borg, BorgUiKey.Key, viewer);
            Assert.That(ui.IsUiOpen(borg, BorgUiKey.Key), "The viewer could not open the cyborg's window.");
        });

        // Open: a charge change that moves the shown percent reaches the window.
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() => Assert.That(battery.TryUseCharge(cell, step), "The cell has charge to drain."));
        await pair.RunTicksSync(2);
        var opened = entMan.GetComponent<UserInterfaceComponent>(borg).LastModifiedTick;
        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<BorgBuiState>(borg, BorgUiKey.Key, out var state), "An open window got no state.");
            var cellBattery = entMan.GetComponent<BatteryComponent>(cell);
            Assert.That(state!.ChargePercent, Is.EqualTo(cellBattery.CurrentCharge / cellBattery.MaxCharge).Within(0.005f),
                "The open window does not show the drained charge.");
        });

        // Open: a change too small to move the shown percent is not sent.
        await server.WaitAssertion(() => Assert.That(battery.TryUseCharge(cell, step * 0.01f)));
        await pair.RunTicksSync(2);
        Assert.That(entMan.GetComponent<UserInterfaceComponent>(borg).LastModifiedTick, Is.EqualTo(opened),
            "A charge change that shows as the same percent sent the cyborg's UI states again.");

        // Closed: no charge change is sent, however large.
        await server.WaitPost(() => ui.CloseUi(borg, BorgUiKey.Key, viewer));
        await pair.RunTicksSync(2);
        var closed = entMan.GetComponent<UserInterfaceComponent>(borg).LastModifiedTick;
        for (var i = 0; i < 5; i++)
        {
            await server.WaitAssertion(() => Assert.That(battery.TryUseCharge(cell, step)));
            await pair.RunTicksSync(1);
        }

        Assert.That(entMan.GetComponent<UserInterfaceComponent>(borg).LastModifiedTick, Is.EqualTo(closed),
            "A charge change with the cyborg's window closed sent its UI states again.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClosedCanisterHoldsNoUiState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        EntityUid canister = default;

        await server.WaitPost(() => canister = entMan.SpawnEntity(Canister, map.GridCoords));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
            Assert.That(ui.TryGetUiState<GasCanisterBoundUserInterfaceState>(canister, GasCanisterUiKey.Key, out _), Is.False,
                "A canister nobody has open holds a UI state."));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClosedHelmHoldsNoUiState()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        EntityUid helm = default;

        await server.WaitPost(() => helm = entMan.SpawnEntity(Helm, map.GridCoords));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
            Assert.That(ui.TryGetUiState<ShuttleBoundUserInterfaceState>(helm, ShuttleConsoleUiKey.Key, out _), Is.False,
                "A shuttle console nobody has open holds a helm state."));

        await pair.CleanReturnAsync();
    }
}
