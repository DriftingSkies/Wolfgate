#nullable enable
using System.Linq;
using Content.Client._WF.MameArcade;
using Content.Server._WF.MameArcade;
using Content.Shared._WF.MameArcade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.MameArcade;

/// <summary>The cabinet seats two players only when their ROMs match, and opens its window.</summary>
[TestOf(typeof(WFMameArcadeSystem))]
public sealed class WFMameArcadeTest
{
    private const string Kof94 = "kof94:9036d879";

    [Test]
    public async Task SeatsTwoPlayersWithMatchingRoms()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var system = entMan.System<WFMameArcadeSystem>();
            var uid = entMan.SpawnEntity("WFMameArcade", map.GridCoords);
            var cabinet = new Entity<WFMameArcadeComponent>(uid, entMan.GetComponent<WFMameArcadeComponent>(uid));
            var host = entMan.SpawnEntity(null, map.GridCoords);
            var guest = entMan.SpawnEntity(null, map.GridCoords);
            var other = entMan.SpawnEntity(null, map.GridCoords);

            Assert.That(system.TryHost(cabinet, host, "kof94", Kof94));
            Assert.That(system.TryHost(cabinet, other, "mslug", "mslug:9036d879"), Is.False, "One lobby per cabinet");
            Assert.That(system.TryJoin(cabinet, guest, "kof94:e72943de"), Is.False, "A different BIOS would desync");
            Assert.That(system.TryJoin(cabinet, host, Kof94), Is.False, "The host can't join their own game");
            Assert.That(system.Partner(cabinet.Comp, host), Is.Null, "Nobody plays until player 2 joins");

            Assert.That(system.TryJoin(cabinet, guest, Kof94));
            Assert.That(system.Partner(cabinet.Comp, host), Is.EqualTo(guest));
            Assert.That(system.Partner(cabinet.Comp, guest), Is.EqualTo(host));
            Assert.That(system.Partner(cabinet.Comp, other), Is.Null);
            Assert.That(system.TryJoin(cabinet, other, Kof94), Is.False, "Two seats only");

            system.Leave(cabinet, other);
            Assert.That(cabinet.Comp.Guest, Is.EqualTo(guest), "A bystander leaving changes nothing");

            system.Leave(cabinet, guest);
            Assert.That(cabinet.Comp.Host, Is.Null);
            Assert.That(system.TryHost(cabinet, other, "mslug", "mslug:9036d879"), Is.False, "Player 1 still plays on alone");

            system.Leave(cabinet, host);
            Assert.That(system.TryHost(cabinet, other, "mslug", "mslug:9036d879"), "The cabinet is free again");

            Assert.That(system.TryHost(cabinet, host, "", Kof94), Is.False);
            entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShowsOneGameToItsWatchers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var system = entMan.System<WFMameArcadeSystem>();
            var uid = entMan.SpawnEntity("WFMameArcade", map.GridCoords);
            var cabinet = new Entity<WFMameArcadeComponent>(uid, entMan.GetComponent<WFMameArcadeComponent>(uid));
            var comp = cabinet.Comp;
            var solo = entMan.SpawnEntity(null, map.GridCoords);
            var host = entMan.SpawnEntity(null, map.GridCoords);
            var guest = entMan.SpawnEntity(null, map.GridCoords);
            var watcher = entMan.SpawnEntity(null, map.GridCoords);

            Assert.That(system.TryWatch(cabinet, watcher), Is.False, "Nothing to watch yet");
            Assert.That(system.TryShowSolo(cabinet, solo, "mslug"));
            Assert.That(system.TryShowSolo(cabinet, host, "kof94"), Is.False, "One game on the screen");
            Assert.That(system.TryWatch(cabinet, solo), Is.False, "Players don't watch their own game");
            Assert.That(system.TryWatch(cabinet, watcher));
            Assert.That(comp.Watchers, Is.EquivalentTo(new[] { watcher }));

            Assert.That(system.TryHost(cabinet, host, "kof94", Kof94), Is.False, "No 2-player game while someone plays");
            system.Leave(cabinet, solo);

            Assert.That(system.TryHost(cabinet, host, "kof94", Kof94));
            Assert.That(system.TryShowSolo(cabinet, solo, "mslug"), Is.False, "No solo game while a 2-player game is open");
            Assert.That(system.TryJoin(cabinet, guest, Kof94));
            Assert.That(comp.Player, Is.EqualTo(host), "A 2-player game shows player 1's screen");
            Assert.That(comp.Watchers, Is.Empty, "A different game starts with no audience");

            Assert.That(system.TryWatch(cabinet, watcher));
            Assert.That(system.TryWatch(cabinet, guest), Is.False);
            system.Leave(cabinet, host);
            Assert.That(comp.Player, Is.EqualTo(guest), "Player 2 plays on alone, still on the screen");
            Assert.That(comp.Watchers, Is.EquivalentTo(new[] { watcher }), "The audience keeps watching the same game");

            system.Leave(cabinet, watcher);
            Assert.That(comp.Watchers, Is.Empty);
            system.Leave(cabinet, guest);
            Assert.That(comp.Player, Is.Null);
            Assert.That(system.TryShowSolo(cabinet, solo, "mslug"), "The screen is free again");

            server.CfgMan.SetCVar(WFMameArcadeCVars.VideoRate, 0);
            Assert.That(system.TryWatch(cabinet, watcher), Is.False, "Watching can be turned off");
            server.CfgMan.SetCVar(WFMameArcadeCVars.VideoRate, WFMameArcadeCVars.VideoRate.DefaultValue);

            entMan.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CritPlayerLeavesTheCabinet()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        Entity<WFMameArcadeComponent> cabinet = default;
        EntityUid player = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            player = entMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), player);
            var uid = entMan.SpawnEntity("WFMameArcade", map.GridCoords);
            cabinet = (uid, entMan.GetComponent<WFMameArcadeComponent>(uid));
            server.System<SharedUserInterfaceSystem>().OpenUi(uid, MameArcadeUiKey.Key, player);
            Assert.That(server.System<WFMameArcadeSystem>().TryShowSolo(cabinet, player, "mslug"));
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            server.System<MobStateSystem>().ChangeMobState(player, MobState.Critical);
            Assert.That(server.System<SharedUserInterfaceSystem>().IsUiOpen(cabinet.Owner, MameArcadeUiKey.Key, player), Is.False,
                "Going into crit closes the cabinet");
            Assert.That(cabinet.Comp!.Player, Is.Null, "Their game leaves the screen");
        });

        await pair.RunTicksSync(10);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GhostsWatchWithoutAsking()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();
        Entity<WFMameArcadeComponent> cabinet = default;
        EntityUid ghost = default;
        EntityUid someone = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            ghost = entMan.SpawnEntity("MobObserver", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), ghost);
            var uid = entMan.SpawnEntity("WFMameArcade", map.GridCoords);
            cabinet = (uid, entMan.GetComponent<WFMameArcadeComponent>(uid));
            someone = entMan.SpawnEntity(null, map.GridCoords);
            server.System<SharedUserInterfaceSystem>().OpenUi(uid, MameArcadeUiKey.Key, ghost);
        });

        await pair.RunTicksSync(30);

        MameArcadeWindow Window() => client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<MameArcadeWindow>()
            .Single();

        await client.WaitAssertion(() =>
        {
            Assert.That(Window().Spectator, "A ghost's buttons never reach the server");
            Assert.That(Window().Watching, Is.False, "Nothing is on the screen yet");
        });

        await server.WaitPost(() => server.System<WFMameArcadeSystem>().TryShowSolo(cabinet, someone, "mslug"));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
            Assert.That(cabinet.Comp!.Watchers, Does.Contain(ghost), "The server counts the ghost in when a game starts"));
        await client.WaitAssertion(() => Assert.That(Window().Watching, "The ghost's window watches the new game"));

        await server.WaitPost(() =>
        {
            var system = server.System<WFMameArcadeSystem>();
            system.Leave(cabinet, someone);
            system.TryShowSolo(cabinet, someone, "kof98");
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
            Assert.That(cabinet.Comp!.Watchers, Does.Contain(ghost), "The ghost stays in the audience for the next game"));
        await client.WaitAssertion(() => Assert.That(Window().Watching, "The ghost's window follows the next game"));

        await client.WaitPost(() => Window().Close());
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
            Assert.That(cabinet.Comp!.Watchers, Does.Not.Contain(ghost), "Closing the window leaves the audience"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CabinetOpensWindow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var player = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(server.PlayerMan.Sessions.Single(), player);
            var cabinet = server.EntMan.SpawnEntity("WFMameArcade", map.GridCoords);
            server.System<SharedUserInterfaceSystem>().OpenUi(cabinet, MameArcadeUiKey.Key, player);
        });

        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            var window = client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
                .OfType<MameArcadeWindow>()
                .SingleOrDefault();

            Assert.That(window, Is.Not.Null, "The cabinet did not open its window");
            window!.Close();
        });

        await pair.CleanReturnAsync();
    }
}
