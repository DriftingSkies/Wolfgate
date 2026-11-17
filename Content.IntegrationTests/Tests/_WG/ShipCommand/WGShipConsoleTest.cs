#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests._WG.ShipAccess;
using Content.Server._WG.ShipCommand;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>
/// The ship command console: a stranger sees only that they aren't cleared, an officer sees the ship and changes
/// nothing, and the owner changes zones, switches, the lists and the price rules through it, with the service log
/// theirs alone to read.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipConsoleSystem))]
public sealed class WGShipConsoleTest
{
    private const string Zone = "core";

    [Test]
    public async Task EachStandingGetsItsShare()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var consoles = entMan.System<WGShipConsoleSystem>();
        var command = entMan.System<WGShipCommandSystem>();
        var keys = entMan.System<WGShipAccessSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WGShipAccessComponent>(grid);
            var cmd = (grid, entMan.EnsureComponent<WGShipCommandComponent>(grid));
            var zones = entMan.EnsureComponent<WGShipZonesComponent>(grid);
            zones.Zones.Add(new WGShipZone { Id = Zone, Name = "wg-ship-zone-test", Mode = WGShipZoneMode.Public });
            entMan.Dirty(grid, zones);

            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, grid);
            var (officer, officerCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            var (stranger, _) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 3);
            var mindSys = entMan.System<SharedMindSystem>();
            var mind = mindSys.CreateMind(null, "Random Stranger");
            mindSys.TransferTo(mind, stranger, mind: mind.Comp);
            Assert.That(keys.TryGetKey(officerCard, out var officerKey), Is.True);
            Assert.That(command.AddPerson(cmd, WGShipCommandList.Officers, officerKey, null, "Ben Ortiz"), Is.True);

            var console = entMan.SpawnEntity("WGComputerShipCommand", map.GridCoords);

            var seen = consoles.State(console, stranger);
            Assert.Multiple(() =>
            {
                Assert.That(seen.Commanded, Is.True);
                Assert.That(seen.CanView, Is.False, "A stranger isn't cleared.");
                Assert.That(seen.Zones, Is.Empty, "And is shown nothing of the ship.");
            });

            seen = consoles.State(console, officer);
            Assert.Multiple(() =>
            {
                Assert.That(seen.CanView, Is.True, "An officer sees the console.");
                Assert.That(seen.CanConfigure, Is.False, "But changes nothing.");
                Assert.That(seen.CanSeeLog, Is.False, "And the log is not his.");
                Assert.That(seen.Zones.Select(z => z.Id), Is.EquivalentTo(new[] { Zone }));
                Assert.That(seen.Officers.Select(p => p.Name), Is.EquivalentTo(new[] { "Ben Ortiz" }));
                Assert.That(seen.Aboard.Select(p => entMan.GetEntity(p.Entity)), Does.Contain(stranger),
                    "Everyone aboard with a mind is listed to be put on a list.");
            });
            Assert.That(consoles.SetSwitch(console, officer, WGShipConsoleSwitch.DocksWelcome, false), Is.False);
            Assert.That(cmd.Item2.DocksWelcome, Is.True, "The officer's switch did nothing.");

            seen = consoles.State(console, owner);
            Assert.Multiple(() =>
            {
                Assert.That(seen.Authority, Is.EqualTo(WGShipAuthority.Owner));
                Assert.That(seen.CanConfigure, Is.True);
                Assert.That(seen.CanSeeLog, Is.True);
            });

            entMan.EventBus.RaiseLocalEvent(console, new WGShipConsoleZoneMessage(Zone, WGShipZoneMode.Locked) { Actor = owner });
            Assert.That(zones.Zones[0].Mode, Is.EqualTo(WGShipZoneMode.Locked), "The owner switches a zone.");

            Assert.That(consoles.SetSwitch(console, owner, WGShipConsoleSwitch.DocksWelcome, false), Is.True);
            Assert.That(cmd.Item2.DocksWelcome, Is.False, "And the crew's welcome for docking ships.");

            Assert.That(consoles.AddPerson(console, owner, stranger, WGShipCommandList.Barred), Is.True);
            Assert.That(command.GetAuthority(stranger, grid), Is.EqualTo(WGShipAuthority.Barred), "The stranger is barred by their card.");
            entMan.EventBus.RaiseLocalEvent(console, new WGShipConsoleRemovePersonMessage(WGShipCommandList.Barred, 0, "Somebody Else") { Actor = owner });
            Assert.That(command.GetAuthority(stranger, grid), Is.EqualTo(WGShipAuthority.Barred), "A stale row removes nobody.");
            entMan.EventBus.RaiseLocalEvent(console, new WGShipConsoleRemovePersonMessage(WGShipCommandList.Barred, 0, "Random Stranger") { Actor = owner });
            Assert.That(command.GetAuthority(stranger, grid), Is.EqualTo(WGShipAuthority.Guest), "And taken off again.");

            var free = new WGShipConsoleSetRuleMessage("crew", WGShipPriceAudience.Staff, null, null, WGShipServiceKind.Medical, 0f);
            Assert.That(consoles.SetRule(console, owner, free), Is.True);
            Assert.That(cmd.Item2.PriceRules.Select(r => r.Name), Does.Contain("crew"));
            var bad = new WGShipConsoleSetRuleMessage("owner", WGShipPriceAudience.Everyone, null, null, WGShipServiceKind.All, 0f);
            Assert.That(consoles.SetRule(console, owner, bad), Is.False, "\"owner\" names the owner's free-service switch.");
            Assert.That(consoles.SetRule(console, officer, free), Is.False, "An officer sets no rules.");

            entMan.DeleteEntity(console);
        });

        await pair.CleanReturnAsync();
    }
}
