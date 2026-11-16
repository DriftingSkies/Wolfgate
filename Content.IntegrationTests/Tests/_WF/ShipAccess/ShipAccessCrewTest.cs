using System;
using Content.IntegrationTests.Pair;
using Content.Server._WF.ShipAccess;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>Live crew credentials remain distinct from ownership and permanent guests.</summary>
[TestFixture]
public sealed class ShipAccessCrewTest
{
    [Test]
    public async Task CrewModeAndDoorRuleFollowMembershipWithoutGrantingOwnership()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var access = em.System<WFShipAccessServerSystem>();
            var credentials = em.System<WFShipAccessSystem>();
            var readers = em.System<AccessReaderSystem>();
            var hands = em.System<SharedHandsSystem>();
            var door = em.SpawnEntity("AirlockShuttle", map.GridCoords);
            var secure = em.SpawnEntity("LockableButtonArmory", map.GridCoords);
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Owner", 1);
            var (crew, card) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Crew member", 2);
            var (guest, _) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Guest", 3);
            var (stranger, _) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Stranger", 4);
            ShipAccessTest.GiveDeed(em, deed, map.Grid.Owner);
            var ship = new Entity<WFShipAccessComponent>(map.Grid.Owner, em.EnsureComponent<WFShipAccessComponent>(map.Grid.Owner));
            Assert.That(credentials.TryGetKey(card, out var key), Is.True);
            access.SetLocked(ship, true);
            access.TryAddPerson(ship, guest);
            Assert.That(access.SetMode(ship, WFShipAccessMode.Crew), Is.False, "An unbound ship cannot enable crew access.");
            access.SetCrew(ship, 1, "Rescue team", new[] { key });
            Assert.That(access.SetMode(ship, WFShipAccessMode.Crew), Is.True);
            Assert.That(readers.IsAllowed(crew, door), Is.True);
            Assert.That(readers.IsAllowed(owner, door), Is.True);
            Assert.That(readers.IsAllowed(guest, door), Is.True);
            Assert.That(readers.IsAllowed(stranger, door), Is.False);
            Assert.That(credentials.IsOwner(crew, ship), Is.False, "Crew access never grants access-management rights.");
            Assert.That(ship.Comp.AllowList.Count, Is.EqualTo(1), "Automatic crew cards are not permanent guests.");

            access.SetDoorRule(ship, door, WFDoorAccessRule.OwnerOnly);
            Assert.That(readers.IsAllowed(crew, door), Is.False);
            Assert.That(readers.IsAllowed(owner, door), Is.True);
            access.SetMode(ship, WFShipAccessMode.Private);
            access.SetDoorRule(ship, door, WFDoorAccessRule.Crew);
            Assert.That(readers.IsAllowed(crew, door), Is.True, "Crew door rules override Private ship mode.");
            Assert.That(readers.IsAllowed(guest, door), Is.False);
            access.SetLocked(ship, false);
            Assert.That(readers.IsAllowed(stranger, door), Is.False, "Crew doors remain restricted on unlocked ships.");
            Assert.That(readers.IsAllowed(crew, secure), Is.False, "Private mode does not add crew to secure default readers.");
            access.SetMode(ship, WFShipAccessMode.Crew);
            Assert.That(readers.IsAllowed(crew, secure), Is.True);
            access.SetCrew(ship, 1, "Rescue team", Array.Empty<WFShipAccessKey>());
            Assert.That(readers.IsAllowed(crew, door), Is.False, "A departed member loses their door rule access immediately.");
            Assert.That(readers.IsAllowed(crew, secure), Is.False, "A departed member loses unlocked secure-reader access too.");
            Assert.That(readers.IsAllowed(owner, door), Is.True);
            Assert.That(readers.IsAllowed(guest, secure), Is.True, "Explicit guest permission survives crew changes.");
            access.SetLocked(ship, true);
            access.SetDoorRule(ship, door, WFDoorAccessRule.Default);
            Assert.That(readers.IsAllowed(crew, door), Is.False, "Locked default readers also revoke crew access.");
            Assert.That(readers.IsAllowed(guest, door), Is.True);
            access.SetCrew(ship, 1, "Rescue team", new[] { key });
            Assert.That(readers.IsAllowed(crew, door), Is.True, "Later arrivals receive access without manual listing.");
            access.ClearForResale(ship);
            Assert.That(em.HasComponent<WFShipAccessComponent>(ship), Is.False, "Resale removes the crew link and credentials.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleRequiresOwnerAndValidCrewForCrewSettings()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var access = em.System<WFShipAccessServerSystem>();
            var hands = em.System<SharedHandsSystem>();
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Owner", 1);
            var (stranger, _) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Stranger", 2);
            ShipAccessTest.GiveDeed(em, deed, map.Grid.Owner);
            var console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var door = em.SpawnEntity("AirlockShuttle", map.GridCoords);
            var ship = new Entity<WFShipAccessComponent>(map.Grid.Owner, em.EnsureComponent<WFShipAccessComponent>(map.Grid.Owner));
            void Send(BoundUserInterfaceMessage message, EntityUid actor)
            {
                message.Actor = actor;
                message.UiKey = ShuttleConsoleUiKey.Key;
                message.Entity = em.GetNetEntity(console);
                em.EventBus.RaiseLocalEvent(console, (object) message);
            }
            Send(new WFShipAccessSetModeMessage(WFShipAccessMode.Crew), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WFShipAccessMode.Private));
            Send(new WFShipAccessSetDoorRuleMessage(em.GetNetEntity(door), WFDoorAccessRule.Crew), owner);
            Assert.That(em.System<WFShipAccessSystem>().GetRule(door), Is.EqualTo(WFDoorAccessRule.Default));
            access.SetCrew(ship, 1, "Crew", Array.Empty<WFShipAccessKey>());
            Send(new WFShipAccessSetModeMessage(WFShipAccessMode.Crew), stranger);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WFShipAccessMode.Private));
            Send(new WFShipAccessSetDoorRuleMessage(em.GetNetEntity(door), WFDoorAccessRule.Crew), stranger);
            Assert.That(em.System<WFShipAccessSystem>().GetRule(door), Is.EqualTo(WFDoorAccessRule.Default));
            Send(new WFShipAccessSetModeMessage(WFShipAccessMode.Crew), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WFShipAccessMode.Crew));
            Send(new WFShipAccessSetDoorRuleMessage(em.GetNetEntity(door), WFDoorAccessRule.Crew), owner);
            Assert.That(em.System<WFShipAccessSystem>().GetRule(door), Is.EqualTo(WFDoorAccessRule.Crew));
            Send(new WFShipAccessSetModeMessage((WFShipAccessMode) 255), owner);
            Send(new WFShipAccessSetModeMessage(WFShipAccessMode.Faction), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WFShipAccessMode.Crew), "Invalid or unavailable modes preserve the selected mode.");
            Send(new WFShipAccessSetAllDoorRulesMessage((WFDoorAccessRule) 255), owner);
            Assert.That(em.System<WFShipAccessSystem>().GetRule(door), Is.EqualTo(WFDoorAccessRule.Crew));

            // Public is staff's to set: the owner's console can't ask for it, but can leave it.
            Send(new WFShipAccessSetModeMessage(WFShipAccessMode.Public), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WFShipAccessMode.Crew), "An owner can't make the ship public from the console.");
            Assert.That(access.SetMode(ship, WFShipAccessMode.Public), Is.True);
            Send(new WFShipAccessSetModeMessage(WFShipAccessMode.Private), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WFShipAccessMode.Private), "The owner can switch a public ship back.");
        });
        await pair.CleanReturnAsync();
    }
}
