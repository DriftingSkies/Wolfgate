using System;
using Content.IntegrationTests.Pair;
using Content.Server._WG.ShipAccess;
using Content.Shared._WG.ShipAccess;
using Content.Shared.Access.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipAccess;

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
            var access = em.System<WGShipAccessServerSystem>();
            var credentials = em.System<WGShipAccessSystem>();
            var readers = em.System<AccessReaderSystem>();
            var hands = em.System<SharedHandsSystem>();
            var door = em.SpawnEntity("AirlockShuttle", map.GridCoords);
            var secure = em.SpawnEntity("LockableButtonArmory", map.GridCoords);
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Owner", 1);
            var (crew, card) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Crew member", 2);
            var (guest, _) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Guest", 3);
            var (stranger, _) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Stranger", 4);
            ShipAccessTest.GiveDeed(em, deed, map.Grid.Owner);
            var ship = new Entity<WGShipAccessComponent>(map.Grid.Owner, em.EnsureComponent<WGShipAccessComponent>(map.Grid.Owner));
            Assert.That(credentials.TryGetKey(card, out var key), Is.True);
            access.SetLocked(ship, true);
            access.TryAddPerson(ship, guest);
            Assert.That(access.SetMode(ship, WGShipAccessMode.Crew), Is.False, "An unbound ship cannot enable crew access.");
            access.SetCrew(ship, 1, "Rescue team", new[] { key });
            Assert.That(access.SetMode(ship, WGShipAccessMode.Crew), Is.True);
            Assert.That(readers.IsAllowed(crew, door), Is.True);
            Assert.That(readers.IsAllowed(owner, door), Is.True);
            Assert.That(readers.IsAllowed(guest, door), Is.True);
            Assert.That(readers.IsAllowed(stranger, door), Is.False);
            Assert.That(credentials.IsOwner(crew, ship), Is.False, "Crew access never grants access-management rights.");
            Assert.That(ship.Comp.AllowList.Count, Is.EqualTo(1), "Automatic crew cards are not permanent guests.");

            access.SetDoorRule(ship, door, WGDoorAccessRule.OwnerOnly);
            Assert.That(readers.IsAllowed(crew, door), Is.False);
            Assert.That(readers.IsAllowed(owner, door), Is.True);
            access.SetMode(ship, WGShipAccessMode.Private);
            access.SetDoorRule(ship, door, WGDoorAccessRule.Crew);
            Assert.That(readers.IsAllowed(crew, door), Is.True, "Crew door rules override Private ship mode.");
            Assert.That(readers.IsAllowed(guest, door), Is.False);
            access.SetLocked(ship, false);
            Assert.That(readers.IsAllowed(stranger, door), Is.False, "Crew doors remain restricted on unlocked ships.");
            Assert.That(readers.IsAllowed(crew, secure), Is.False, "Private mode does not add crew to secure default readers.");
            access.SetMode(ship, WGShipAccessMode.Crew);
            Assert.That(readers.IsAllowed(crew, secure), Is.True);
            access.SetCrew(ship, 1, "Rescue team", Array.Empty<WGShipAccessKey>());
            Assert.That(readers.IsAllowed(crew, door), Is.False, "A departed member loses their door rule access immediately.");
            Assert.That(readers.IsAllowed(crew, secure), Is.False, "A departed member loses unlocked secure-reader access too.");
            Assert.That(readers.IsAllowed(owner, door), Is.True);
            Assert.That(readers.IsAllowed(guest, secure), Is.True, "Explicit guest permission survives crew changes.");
            access.SetLocked(ship, true);
            access.SetDoorRule(ship, door, WGDoorAccessRule.Default);
            Assert.That(readers.IsAllowed(crew, door), Is.False, "Locked default readers also revoke crew access.");
            Assert.That(readers.IsAllowed(guest, door), Is.True);
            access.SetCrew(ship, 1, "Rescue team", new[] { key });
            Assert.That(readers.IsAllowed(crew, door), Is.True, "Later arrivals receive access without manual listing.");
            access.ClearForResale(ship);
            Assert.That(em.HasComponent<WGShipAccessComponent>(ship), Is.False, "Resale removes the crew link and credentials.");
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
            var access = em.System<WGShipAccessServerSystem>();
            var hands = em.System<SharedHandsSystem>();
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Owner", 1);
            var (stranger, _) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Stranger", 2);
            ShipAccessTest.GiveDeed(em, deed, map.Grid.Owner);
            var console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var door = em.SpawnEntity("AirlockShuttle", map.GridCoords);
            var ship = new Entity<WGShipAccessComponent>(map.Grid.Owner, em.EnsureComponent<WGShipAccessComponent>(map.Grid.Owner));
            void Send(BoundUserInterfaceMessage message, EntityUid actor)
            {
                message.Actor = actor;
                message.UiKey = ShuttleConsoleUiKey.Key;
                message.Entity = em.GetNetEntity(console);
                em.EventBus.RaiseLocalEvent(console, (object) message);
            }
            Send(new WGShipAccessSetModeMessage(WGShipAccessMode.Crew), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WGShipAccessMode.Private));
            Send(new WGShipAccessSetDoorRuleMessage(em.GetNetEntity(door), WGDoorAccessRule.Crew), owner);
            Assert.That(em.System<WGShipAccessSystem>().GetRule(door), Is.EqualTo(WGDoorAccessRule.Default));
            access.SetCrew(ship, 1, "Crew", Array.Empty<WGShipAccessKey>());
            Send(new WGShipAccessSetModeMessage(WGShipAccessMode.Crew), stranger);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WGShipAccessMode.Private));
            Send(new WGShipAccessSetDoorRuleMessage(em.GetNetEntity(door), WGDoorAccessRule.Crew), stranger);
            Assert.That(em.System<WGShipAccessSystem>().GetRule(door), Is.EqualTo(WGDoorAccessRule.Default));
            Send(new WGShipAccessSetModeMessage(WGShipAccessMode.Crew), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WGShipAccessMode.Crew));
            Send(new WGShipAccessSetDoorRuleMessage(em.GetNetEntity(door), WGDoorAccessRule.Crew), owner);
            Assert.That(em.System<WGShipAccessSystem>().GetRule(door), Is.EqualTo(WGDoorAccessRule.Crew));
            Send(new WGShipAccessSetModeMessage((WGShipAccessMode) 255), owner);
            Send(new WGShipAccessSetModeMessage(WGShipAccessMode.Faction), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WGShipAccessMode.Crew), "Invalid or unavailable modes preserve the selected mode.");
            Send(new WGShipAccessSetAllDoorRulesMessage((WGDoorAccessRule) 255), owner);
            Assert.That(em.System<WGShipAccessSystem>().GetRule(door), Is.EqualTo(WGDoorAccessRule.Crew));

            // Public is staff's to set: the owner's console can't ask for it, but can leave it.
            Send(new WGShipAccessSetModeMessage(WGShipAccessMode.Public), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WGShipAccessMode.Crew), "An owner can't make the ship public from the console.");
            Assert.That(access.SetMode(ship, WGShipAccessMode.Public), Is.True);
            Send(new WGShipAccessSetModeMessage(WGShipAccessMode.Private), owner);
            Assert.That(ship.Comp.Mode, Is.EqualTo(WGShipAccessMode.Private), "The owner can switch a public ship back.");
        });
        await pair.CleanReturnAsync();
    }
}
