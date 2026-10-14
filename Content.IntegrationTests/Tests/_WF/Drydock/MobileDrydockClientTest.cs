#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Drydock;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Drydock;
using Content.Server.Power.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Robust.Client.GameObjects;
using Robust.Client.GameStates;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>
/// A mobile drydock console open on a connected client keeps one filled window, its entities and its crane animation
/// while a ship pulls into the berth, its silo is refilled and a repair runs.
/// </summary>
[TestFixture]
public sealed class MobileDrydockClientTest
{
    /// <summary>Where the damaged ship waits before it pulls into the berth.</summary>
    private static readonly Vector2 ShipWaiting = new(-300f, 80f);

    /// <summary>What the client holds for the console and the crane on one tick.</summary>
    private readonly record struct ClientView(
        EntityUid Console,
        EntityUid Crane,
        WFDrydockCraneComponent? CraneComp,
        BoundUserInterface? Bui,
        int Buis,
        int Windows,
        WFDrydockConsoleWindow? Window,
        bool Filled);

    /// <summary>Reads the client's console, crane, open drydock interfaces and windows.</summary>
    private static ClientView Look(TestPair pair, NetEntity console, NetEntity crane, int refits)
    {
        var em = pair.Client.EntMan;
        var ui = pair.Client.System<SharedUserInterfaceSystem>();
        em.TryGetEntity(console, out var clientConsole);
        em.TryGetEntity(crane, out var clientCrane);

        BoundUserInterface? bui = null;
        if (clientConsole is { } consoleUid)
            ui.TryGetOpenUi(consoleUid, WFDrydockConsoleUiKey.Key, out bui);

        var buis = 0;
        var query = em.AllEntityQueryEnumerator<UserInterfaceComponent>();
        while (query.MoveNext(out _, out var comp))
        {
            buis += comp.ClientOpenInterfaces.Values.Count(b => b is WFDrydockConsoleBoundUserInterface);
        }

        WFDrydockCraneComponent? craneComp = null;
        if (clientCrane is { } craneUid)
            em.TryGetComponent(craneUid, out craneComp);

        var windows = pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<WFDrydockConsoleWindow>()
            .ToList();
        var window = windows.FirstOrDefault();

        return new ClientView(clientConsole ?? EntityUid.Invalid, clientCrane ?? EntityUid.Invalid, craneComp, bui, buis,
            windows.Count, window, window != null && Filled(window, refits));
    }

    /// <summary>Whether the window has its button captions and a row for each of the console's refits.</summary>
    private static bool Filled(WFDrydockConsoleWindow window, int refits)
    {
        return !string.IsNullOrEmpty(window.FindControl<Button>("RepairButton").Text)
               && !string.IsNullOrEmpty(window.FindControl<Button>("BerthLightsButton").Text)
               && window.FindControl<BoxContainer>("RefitContainer").ChildCount == refits;
    }

    [Test]
    public async Task ConsoleWindowSurvivesBerthSiloAndJob()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var materials = server.System<SharedMaterialStorageSystem>();
        var xforms = server.System<SharedTransformSystem>();

        // Test pairs turn PVS off. In game it is on, so a full state is budgeted and the client re-creates what it misses.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));

        // A full state re-sends every entity, and the client deletes and re-creates what it no longer gets.
        var fullStates = 0;
        void OnStateApplied(GameStateAppliedArgs args)
        {
            if (args.AppliedState.FromSequence == GameTick.Zero)
                fullStates++;
        }

        EntityUid host = default, crane = default, console = default, ship = default, payer = default, silo = default;
        WFDrydockBerth berth = default;
        await server.WaitAssertion(() =>
        {
            (host, crane, console) = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out berth), Is.True, "The crane should make a berth.");

            // The crane starts with some stock, so the window lists stored materials from the first state.
            Assert.That(materials.TryChangeMaterialAmount((crane, em.GetComponent<MaterialStorageComponent>(crane)),
                new Dictionary<string, int> { ["Steel"] = 500 }), Is.True, "The crane should take its stock.");

            silo = MobileDrydockTest.SpawnSilo(pair, host);
            ship = DrydockTest.CreateDamagedShip(pair, map.MapId, ShipWaiting);
            payer = DrydockTest.CreatePayer(pair, map.MapId);
            xforms.SetCoordinates(payer, new EntityCoordinates(host, 4.5f, 0.5f));
        });

        await pair.RunTicksSync(5);
        await MobileDrydockTest.WaitForCranePower(pair, crane);
        await DrydockTest.RunUntil(pair, 100, 10, () => em.GetComponent<ApcPowerReceiverComponent>(silo).Powered);

        await server.WaitAssertion(() =>
        {
            em.EventBus.RaiseLocalEvent(silo, new ToggleOreSiloClientMessage(em.GetNetEntity(crane)));
            Assert.That(em.GetComponent<OreSiloClientComponent>(crane).Silo, Is.EqualTo(silo), "The crane should link to the silo.");
            Assert.That(server.System<SharedUserInterfaceSystem>().TryOpenUi(console, WFDrydockConsoleUiKey.Key, payer), Is.True,
                "The payer should open the console.");
        });

        await pair.RunTicksSync(10);

        var netConsole = em.GetNetEntity(console);
        var netCrane = em.GetNetEntity(crane);
        var refits = em.GetComponent<WFDrydockConsoleComponent>(console).Refits.Count;
        var gameStates = client.ResolveDependency<IClientGameStateManager>();
        gameStates.GameStateApplied += OnStateApplied;

        ClientView first = default;
        await client.WaitAssertion(() =>
        {
            first = Look(pair, netConsole, netCrane, refits);
            Assert.Multiple(() =>
            {
                Assert.That(first.Bui, Is.Not.Null, "The client should open the console's interface.");
                Assert.That(first.Windows, Is.EqualTo(1), "The client should show one drydock window.");
                Assert.That(first.CraneComp, Is.Not.Null, "The client should have the crane.");
            });
        });

        var views = new List<(string Stage, ClientView View)> { ("Opened", first) };
        var arms = new List<float>();

        async Task Watch(string stage, int ticks, bool trackArm = false)
        {
            for (var i = 0; i < ticks; i++)
            {
                await pair.RunTicksSync(1);
                await client.WaitPost(() =>
                {
                    var view = Look(pair, netConsole, netCrane, refits);
                    views.Add((stage, view));
                    if (trackArm && view.CraneComp != null)
                        arms.Add(view.CraneComp.VisualArm);
                });
            }
        }

        // The ship pulls into the berth.
        await server.WaitPost(() => DrydockTest.PlaceOver(pair, ship, host, berth.Area.Center));
        await Watch("Ship arrived", 60);

        // The silo is refilled.
        await server.WaitAssertion(() => MobileDrydockTest.Stock(pair, silo));
        await Watch("Silo refilled", 60);

        // The payer presses repair in whichever window the client has now.
        await client.WaitPost(() => (Look(pair, netConsole, netCrane, refits).Bui ?? first.Bui)?.SendMessage(new WFDrydockRepairMessage()));
        await Watch("Repair sent", 5);
        var jobStarted = false;
        await server.WaitPost(() => jobStarted = em.HasComponent<WFDrydockJobComponent>(console));
        await Watch("Job running", 240, trackArm: true);

        gameStates.GameStateApplied -= OnStateApplied;

        // The arm and boom draw on a rig on the map, which follows the crane, so they are culled with the map, not the grid.
        var rigProblems = new List<string>();
        await client.WaitPost(() =>
        {
            var cem = client.EntMan;
            var clientCrane = cem.GetEntity(netCrane);
            var visuals = client.System<WFDrydockCraneVisualSystem>();
            var cxforms = client.System<SharedTransformSystem>();
            if (!visuals.TryGetRig(clientCrane, out var rig))
            {
                rigProblems.Add("no rig");
                return;
            }

            var craneXform = cem.GetComponent<TransformComponent>(clientCrane);
            var rigXform = cem.GetComponent<TransformComponent>(rig);
            if (rigXform.ParentUid != craneXform.MapUid)
                rigProblems.Add($"rig parent {rigXform.ParentUid} is not the map {craneXform.MapUid}");
            if (Vector2.Distance(cxforms.GetWorldPosition(rigXform), cxforms.GetWorldPosition(craneXform)) > 0.01f)
                rigProblems.Add("rig is not on the crane");
            if (!cem.GetComponent<SpriteComponent>(rig).Visible)
                rigProblems.Add("rig is hidden");
            if (cem.GetComponent<SpriteComponent>(clientCrane).AllLayers.Count() != 2)
                rigProblems.Add("the crane should only draw its body and lights");
        });

        var furthest = arms.Count > 0 ? arms.Max() : 0f;
        var drops = 0;
        for (var i = 1; i < arms.Count; i++)
        {
            if (arms[i] < arms[i - 1] - 0.001f)
                drops++;
        }

        // Per stage, the ticks on which the client lost or duplicated something.
        var problems = new List<string>();
        foreach (var stage in views.Select(v => v.Stage).Distinct())
        {
            var ticks = views.Where(v => v.Stage == stage).Select(v => v.View).ToList();
            void Count(string what, Func<ClientView, bool> bad)
            {
                var n = ticks.Count(bad);
                if (n > 0)
                    problems.Add($"{stage}: {what} on {n}/{ticks.Count} ticks");
            }

            Count("console entity changed", v => v.Console != first.Console);
            Count("crane entity changed", v => v.Crane != first.Crane);
            Count("crane component re-created", v => !ReferenceEquals(v.CraneComp, first.CraneComp));
            Count("interface re-created or closed", v => !ReferenceEquals(v.Bui, first.Bui));
            Count("drydock interfaces != 1", v => v.Buis != 1);
            Count("drydock windows != 1", v => v.Windows != 1);
            Count("window replaced", v => !ReferenceEquals(v.Window, first.Window));
            Count("blank buttons or empty refits", v => !v.Filled);
        }

        var errors = pair.ClientLogHandler.FailingLogs.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty, "The client should keep one filled window and the same entities throughout.");
            Assert.That(rigProblems, Is.Empty, "The crane's arm should draw on a rig on the map.");
            Assert.That(errors, Is.Empty, "The client should log no errors.");
            Assert.That(fullStates, Is.EqualTo(0), "The client should never need a full state.");
            Assert.That(jobStarted, Is.True, "The repair should start from the client's button.");
            Assert.That(furthest, Is.GreaterThan(1f), "The crane's arm should extend over the ship.");
            Assert.That(drops, Is.EqualTo(0), "The crane's arm should never jump back while it extends.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Mirroring a mobile crane on the server flips its body art and arm side on a connected client, on the same tiles.</summary>
    [Test]
    public async Task MirroredCraneRedrawsOnClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid crane = default;
        await server.WaitPost(() => (_, crane, _) = MobileDrydockTest.SpawnMobileYard(pair, map.MapId));
        await pair.RunTicksSync(10);

        var netCrane = em.GetNetEntity(crane);
        EntityUid rigBefore = default;
        await client.WaitAssertion(() =>
        {
            var clientCrane = client.EntMan.GetEntity(netCrane);
            Assert.That(client.System<WFDrydockCraneVisualSystem>().TryGetRig(clientCrane, out rigBefore), Is.True, "The client crane should have a rig.");
        });

        await server.WaitPost(() =>
        {
            var comp = em.GetComponent<WFDrydockCraneComponent>(crane);
            comp.Mirrored = true;
            em.Dirty(crane, comp);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var cem = client.EntMan;
            var clientCrane = cem.GetEntity(netCrane);
            var comp = cem.GetComponent<WFDrydockCraneComponent>(clientCrane);
            var body = cem.GetComponent<SpriteComponent>(clientCrane).AllLayers.OfType<SpriteComponent.Layer>().First();
            Assert.That(client.System<WFDrydockCraneVisualSystem>().TryGetRig(clientCrane, out var rig), Is.True, "The client crane should keep a rig.");
            var arm = cem.GetComponent<SpriteComponent>(rig).AllLayers.OfType<SpriteComponent.Layer>().First();

            Assert.Multiple(() =>
            {
                Assert.That(comp.Mirrored, Is.True, "The client should get the mirrored flag.");
                Assert.That(rig, Is.Not.EqualTo(rigBefore), "The client should rebuild the crane's rig.");
                Assert.That(cem.Deleted(rigBefore) || cem.IsQueuedForDeletion(rigBefore), Is.True, "The old rig should go.");
                Assert.That(body.State.Name, Is.EqualTo("machine_mirrored"), "The body should face the other way.");
                Assert.That(body.Offset, Is.EqualTo(new Vector2(0.5f, -0.5f)), "The body should stay on its tiles.");
                Assert.That(arm.Offset.X, Is.EqualTo(1f), "The arm should leave from the body's other column.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
