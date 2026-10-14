#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Drydock;
using Content.Server._WF.Hailing;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.Hailing;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>
/// A ship hails the drydock; the dockmaster answers and greets, and the pilot picks the drydock window from its answers.
/// </summary>
[TestFixture]
[TestOf(typeof(HailingSystem))]
public sealed class DrydockHailTest
{
    [Test]
    public async Task PilotOpensDrydockServiceOverHail()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var hailing = server.System<HailingSystem>();
        var ui = server.System<SharedUserInterfaceSystem>();

        EntityUid station = default, traffic = default, dockmaster = default, console = default, pilot = default;

        await server.WaitAssertion(() =>
        {
            station = DrydockTest.LoadDrydock(pair, map.MapId).Station;
            dockmaster = DrydockTest.FindOperator(pair, station);
            traffic = FindTraffic(pair, station);
            Assert.That(traffic.IsValid(), "The drydock needs a traffic console on its grid.");
            Assert.That(DrydockTest.HailContactsOn(pair, station), Is.EqualTo(new List<EntityUid> { dockmaster }),
                "The dockmaster should be the drydock's one hail contact.");
        });

        // The POI's addComponents (including PreventPilot) are not applied by a plain grid load, so only power is set here.
        await server.WaitPost(() => SetPowered(pair, traffic));
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(traffic).Powered, Is.True,
                "The traffic console should be powered.");
        });

        // The pilot's ship sits on the test map, with a powered console and a pilot at the helm.
        await server.WaitPost(() =>
        {
            DrydockTest.SetFloor(pair, map.Grid, 3);
            console = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid, new Vector2(1.5f, 0.5f)));
            SetPowered(pair, console);
            pilot = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            ui.OpenUi(console, ShuttleConsoleUiKey.Key, pilot);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.IsUiOpen(console, ShuttleConsoleUiKey.Key, pilot), "The pilot needs the console open.");
            Assert.That(hailing.GetTargets(console).Select(t => t.Ship), Does.Contain(em.GetNetEntity(station)),
                "The drydock should be on the pilot's hail list.");
            hailing.ClearCooldowns(console, station);
            Assert.That(hailing.TryHail(console, pilot, station), Is.True, "The hail should go out.");
        });

        // The dockmaster answers after its delay, within a few seconds of game time.
        bool Connected() => em.TryGetComponent(console, out HailConsoleComponent? hail) && hail.State == HailState.Connected;
        await DrydockTest.RunUntil(pair, 40, 10, Connected);
        Assert.That(Connected(), Is.True, "The dockmaster should answer the hail within a few seconds.");

        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            var hail = em.GetComponent<HailConsoleComponent>(console);
            Assert.That(hail.Peer, Is.EqualTo(traffic), "The call should run from the traffic console.");
            Assert.That(em.GetComponent<HailConsoleComponent>(traffic).Automated, Is.True,
                "The dockmaster should hold its end as an automated console.");
            Assert.That(ui.IsUiOpen(traffic, ShuttleConsoleUiKey.Key, dockmaster), Is.False,
                "The dockmaster should not need the console's window.");
            Assert.That(hail.Options.Select(o => o.Text), Is.EqualTo(new[] { Loc.GetString("drydock-hail-service") }),
                "The dockmaster's service should be the answer on offer.");

            var heard = hail.Log.Where(l => !l.Own).Select(l => l.Text).ToList();
            var greetings = new[] { "drydock-hail-greeting-1", "drydock-hail-greeting-2", "drydock-hail-greeting-3" }
                .Select(key => Loc.GetString(key,
                    ("name", em.GetComponent<MetaDataComponent>(dockmaster).EntityName),
                    ("location", hailing.ShipLabel(console, station)),
                    ("ship", hailing.ShipLabel(traffic, map.Grid))))
                .ToList();
            Assert.That(heard.Any(text => greetings.Contains(text)), $"The dockmaster should greet the caller by name. Heard: {string.Join(" | ", heard)}");
            Assert.That(heard.Where(text => text.Contains('{')), Is.Empty, "No spoken line should keep a raw placeholder.");
            Assert.That(heard, Does.Contain(Loc.GetString("drydock-hail-offer")),
                "A ship with no booking should be offered the drydock's services.");
        });

        await server.WaitAssertion(() =>
        {
            var offered = em.GetComponent<HailConsoleComponent>(console).Options[0].Id;
            DrydockTest.RaiseUiMessage(pair, console, ShuttleConsoleUiKey.Key, new HailChooseMessage(offered), pilot);

            Assert.That(em.TryGetComponent(console, out HailRelayComponent? relay), "Opening a service should add a relay to the helm.");
            Assert.That(relay!.Links.TryGetValue(WFDrydockConsoleUiKey.Key, out var linked) && linked == dockmaster,
                "The drydock window should be relayed from the dockmaster.");
            Assert.That(ui.IsUiOpen(console, WFDrydockConsoleUiKey.Key, pilot),
                "The drydock window should be open on the helm for the pilot.");
            Assert.That(em.GetComponent<WFDrydockOperatorComponent>(dockmaster).Viewers.TryGetValue(pilot, out var helm) && helm == console,
                "The dockmaster should know the pilot is viewing from the helm.");
        });

        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(ui.IsUiOpen(console, WFDrydockConsoleUiKey.Key, pilot), "The relayed window should stay open during the call.");
            Assert.That(em.GetComponent<HailConsoleComponent>(console).Options, Is.Empty,
                "Nothing should be on offer while the drydock window is open.");

            ui.CloseUi(console, WFDrydockConsoleUiKey.Key, pilot);
        });

        // Closing the window brings the answers back, and the service opens again from them.
        bool Offered() => em.TryGetComponent(console, out HailConsoleComponent? hail) && hail.Options.Count > 0;
        await DrydockTest.RunUntil(pair, 40, 10, Offered);
        Assert.That(Offered(), Is.True, "The dockmaster should offer its service again once the window is closed.");

        await server.WaitAssertion(() =>
        {
            Assert.That(hailing.Choose(console, pilot, em.GetComponent<HailConsoleComponent>(console).Options[0].Id), Is.True);
            Assert.That(ui.IsUiOpen(console, WFDrydockConsoleUiKey.Key, pilot), "The drydock window should open again.");

            hailing.End(console);

            Assert.That(em.HasComponent<HailConsoleComponent>(console), Is.False, "Ending the call should close the pilot's channel.");
            Assert.That(em.HasComponent<HailConsoleComponent>(traffic), Is.False, "Ending the call should close the dockmaster's channel.");
            Assert.That(em.GetComponent<HailRelayComponent>(console).Links, Is.Empty, "The relay link should be gone.");
            Assert.That(ui.IsUiOpen(console, WFDrydockConsoleUiKey.Key, pilot), Is.False, "The drydock window should close on the helm.");
        });

        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<HailRelayComponent>(console).Links, Is.Empty, "No relay should come back after the call.");
            Assert.That(DrydockTest.HailContactsOn(pair, station), Has.Count.EqualTo(1), "The dockmaster should still be there to answer.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Finds the drydock's traffic console on the station grid.</summary>
    private static EntityUid FindTraffic(TestPair pair, EntityUid station)
    {
        var em = pair.Server.EntMan;
        var query = em.EntityQueryEnumerator<ShuttleConsoleComponent, MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var meta, out var xform))
        {
            if (xform.GridUid == station && meta.EntityPrototype?.ID == "WFComputerDrydockTraffic")
                return uid;
        }

        return EntityUid.Invalid;
    }

    /// <summary>Powers a console directly, as the test consoles are not on a power network.</summary>
    private static void SetPowered(TestPair pair, EntityUid console)
    {
        var power = pair.Server.EntMan.GetComponent<ApcPowerReceiverComponent>(console);
        power.NeedsPower = false;
        power.Powered = true;
    }
}
