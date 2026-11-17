#nullable enable
using Content.Server.Chemistry.EntitySystems;
using Content.Server.DeviceLinking.Components;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Lathe;
using Content.Server.SurveillanceCamera;
using Content.Shared.Chemistry;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Lathe;
using Content.Shared.MachineLinking;
using Content.Shared.SurveillanceCamera;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NetTraffic;

/// <summary>
/// Lathes, signal timers, camera monitors and chem masters hold no UI state while nobody has them open, and get one as
/// a window opens.
/// </summary>
[TestFixture]
[TestOf(typeof(LatheSystem))]
[TestOf(typeof(SignalTimerSystem))]
[TestOf(typeof(SurveillanceCameraMonitorSystem))]
[TestOf(typeof(ChemMasterSystem))]
public sealed class WGUiStateTrimMoreTest
{
    private const string Lathe = "Autolathe";
    private const string Timer = "SignalTimer";
    private const string Monitor = "ComputerSurveillanceCameraMonitor";
    private const string ChemMaster = "ChemMaster";
    private const string Beaker = "Beaker";
    private const string Viewer = "MobHuman";

    [Test]
    public async Task LatheStateOnlyWhileOpen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        var lathes = server.System<LatheSystem>();
        EntityUid lathe = default;
        EntityUid viewer = default;

        await server.WaitPost(() =>
        {
            lathe = entMan.SpawnEntity(Lathe, map.GridCoords);
            viewer = entMan.SpawnEntity(Viewer, map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            lathes.UpdateUserInterfaceState(lathe);
            Assert.That(ui.TryGetUiState<LatheUpdateState>(lathe, LatheUiKey.Key, out _), Is.False,
                "A lathe nobody has open holds a UI state.");

            ui.OpenUi(lathe, LatheUiKey.Key, viewer);
            Assert.That(ui.IsUiOpen(lathe, LatheUiKey.Key), "The viewer could not open the lathe.");
            Assert.That(ui.TryGetUiState<LatheUpdateState>(lathe, LatheUiKey.Key, out _),
                "Opening the lathe set no UI state.");
        });

        await server.WaitPost(() => ui.CloseUi(lathe, LatheUiKey.Key, viewer));
        await pair.RunTicksSync(2);
        var closed = entMan.GetComponent<UserInterfaceComponent>(lathe).LastModifiedTick;
        await server.WaitPost(() => lathes.UpdateUserInterfaceState(lathe));
        await pair.RunTicksSync(2);
        Assert.That(entMan.GetComponent<UserInterfaceComponent>(lathe).LastModifiedTick, Is.EqualTo(closed),
            "Updating a closed lathe sent its UI states again.");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SignalTimerStateOnlyWhileOpen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        var timers = server.System<SignalTimerSystem>();
        EntityUid timer = default;
        EntityUid viewer = default;

        await server.WaitPost(() =>
        {
            timer = entMan.SpawnEntity(Timer, map.GridCoords);
            viewer = entMan.SpawnEntity(Viewer, map.GridCoords);
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            timers.Trigger(timer, entMan.GetComponent<SignalTimerComponent>(timer));
            Assert.That(ui.TryGetUiState<SignalTimerBoundUserInterfaceState>(timer, SignalTimerUiKey.Key, out _), Is.False,
                "A timer nobody has open got a UI state when it went off.");

            // The activatable UI raises this right after opening the window.
            ui.OpenUi(timer, SignalTimerUiKey.Key, viewer);
            entMan.EventBus.RaiseLocalEvent(timer, new AfterActivatableUIOpenEvent(viewer, viewer));
            Assert.That(ui.TryGetUiState<SignalTimerBoundUserInterfaceState>(timer, SignalTimerUiKey.Key, out _),
                "Opening the timer set no UI state.");

            timers.Trigger(timer, entMan.GetComponent<SignalTimerComponent>(timer));
            Assert.That(ui.TryGetUiState<SignalTimerBoundUserInterfaceState>(timer, SignalTimerUiKey.Key, out var state));
            Assert.That(state!.TimerStarted, Is.False, "The open window was not told the timer went off.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CameraMonitorStateOnlyWhileOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        var monitors = server.System<SurveillanceCameraMonitorSystem>();
        EntityUid monitor = default;
        EntityUid viewer = default;

        await server.WaitPost(() =>
        {
            monitor = entMan.SpawnEntity(Monitor, map.GridCoords);
            viewer = entMan.SpawnEntity(Viewer, map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, viewer);
        });

        // Its startup pings the camera network; the replies update the UI.
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<SurveillanceCameraMonitorUiState>(monitor, SurveillanceCameraMonitorUiKey.Key, out _),
                Is.False, "A camera monitor nobody has open holds a UI state.");

            // What the activatable UI does as the window opens.
            ui.OpenUi(monitor, SurveillanceCameraMonitorUiKey.Key, viewer);
            monitors.AfterOpenUserInterface(monitor, viewer);
            Assert.That(ui.TryGetUiState<SurveillanceCameraMonitorUiState>(monitor, SurveillanceCameraMonitorUiKey.Key, out _),
                "Opening the camera monitor set no UI state.");

            ui.CloseUi(monitor, SurveillanceCameraMonitorUiKey.Key, viewer);
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChemMasterStateOnlyWhileOpen()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;
        var ui = server.System<UserInterfaceSystem>();
        var slots = server.System<ItemSlotsSystem>();
        EntityUid chem = default;
        EntityUid viewer = default;

        await server.WaitPost(() =>
        {
            chem = entMan.SpawnEntity(ChemMaster, map.GridCoords);
            viewer = entMan.SpawnEntity(Viewer, map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.TryGetUiState<ChemMasterBoundUserInterfaceState>(chem, ChemMasterUiKey.Key, out _), Is.False,
                "A chem master nobody has open holds a UI state.");

            ui.OpenUi(chem, ChemMasterUiKey.Key, viewer);
            Assert.That(ui.TryGetUiState<ChemMasterBoundUserInterfaceState>(chem, ChemMasterUiKey.Key, out _),
                "Opening the chem master set no UI state.");
        });

        await server.WaitPost(() => ui.CloseUi(chem, ChemMasterUiKey.Key, viewer));
        await pair.RunTicksSync(2);
        var closed = entMan.GetComponent<UserInterfaceComponent>(chem).LastModifiedTick;
        await server.WaitAssertion(() =>
        {
            var beaker = entMan.SpawnEntity(Beaker, map.GridCoords);
            Assert.That(slots.TryInsert(chem, SharedChemMaster.InputSlotName, beaker, null), "The beaker did not go in.");
        });
        await pair.RunTicksSync(2);
        Assert.That(entMan.GetComponent<UserInterfaceComponent>(chem).LastModifiedTick, Is.EqualTo(closed),
            "Inserting a beaker into a closed chem master sent its UI states again.");

        await pair.CleanReturnAsync();
    }
}
