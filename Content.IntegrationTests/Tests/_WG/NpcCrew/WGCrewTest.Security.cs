#nullable enable
using System.Numerics;
using System.Linq;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Access.Systems;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Unregistered map ships receive ordinary crew credentials when populated.</summary>
    [Test]
    public async Task CrewEnrollsUnmanagedShip()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WGShipAccessComponent>(deck), Is.False);
            SEntMan.EnsureComponent<Content.Server.Shuttles.Components.ShuttleComponent>(deck);
            var door = SEntMan.SpawnAtPosition(TestDock, new EntityCoordinates(deck, new Vector2(3.5f)));
            var crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "access")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            Assert.That(SEntMan.GetComponent<WGShipAccessComponent>(deck).AllowList, Has.Count.EqualTo(1));
            Assert.That(Server.System<AccessReaderSystem>().IsAllowed(crew, door), Is.True);
        });
    }

    /// <summary>Friendly damage is cancelled before the wound router can apply it.</summary>
    [Test]
    public async Task CrewCannotDamageFriendlyCrew()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crewSystem = Server.System<WGCrewSystem>();
            var one = crewSystem.SpawnCrewman(WGCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(1.5f)), "crew")!.Value;
            var two = crewSystem.SpawnCrewman(WGCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(2.5f)), "crew")!.Value;
            SEntMan.GetComponent<HTNComponent>(one).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(two).Enabled = false;
            var before = SEntMan.GetComponent<DamageableComponent>(two).TotalDamage;
            var hit = new DamageSpecifier();
            hit.DamageDict.Add("Piercing", 20);
            Server.System<DamageableSystem>().TryChangeDamage(two, hit, origin: one);
            Assert.That(SEntMan.GetComponent<DamageableComponent>(two).TotalDamage, Is.EqualTo(before));
        });
    }

    /// <summary>Boarding rules distinguish a warning from explicit hostility.</summary>
    [TestCase(WGCrewSecurityResponse.Warn, false)]
    [TestCase(WGCrewSecurityResponse.Hostile, true)]
    public async Task CrewBoardingRules(WGCrewSecurityResponse response, bool hostile)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        EntityUid crew = default;
        EntityUid visitor = default;
        await Server.WaitAssertion(() =>
        {
            crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(1.5f)), "crew")!.Value;
            visitor = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "visitor")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(visitor).Enabled = false;
            Server.System<NpcFactionSystem>().ClearFactions(visitor);
            SEntMan.EnsureComponent<WGCrewSecurityComponent>(crew).Boarding = response;
        });
        await RunTicks(70);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewSecuritySystem>().IsAuthorized(crew, visitor), Is.False);
            Assert.That(SEntMan.TryGetComponent<Content.Shared.NPC.Components.FactionExceptionComponent>(crew, out var exceptions)
                && exceptions.Hostiles.Any(target => target == visitor), Is.EqualTo(hostile));
        });
    }

    /// <summary>
    /// The audit's brandish case: a commanded ship's crew let its owner carry a gun, past the warning's deadline and
    /// without turning on him; a ship with no command still tells the same player to put it away.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task BrandishRuleDefersToTheShipsCommand(bool commanded)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 7, gravity: true);
        var conduct = Server.System<WGCrewConductSystem>();
        EntityUid crew = default;
        EntityUid gun = default;
        await Server.WaitPost(() =>
        {
            if (commanded)
            {
                SEntMan.EnsureComponent<WGShipAccessComponent>(deck).OwnerUsers.Add(ServerSession.UserId);
                SEntMan.EnsureComponent<WGShipCommandComponent>(deck);
            }

            crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f, 3.5f)), "conduct")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            SEntMan.EnsureComponent<WGCrewSecurityComponent>(crew).Boarding = WGCrewSecurityResponse.Ignore;

            // A player's body is a mob; the bare test body is not.
            SEntMan.EnsureComponent<MobStateComponent>(SPlayer);
            Transform.SetCoordinates(SPlayer, new EntityCoordinates(deck, new Vector2(4.5f, 3.5f)));
            gun = SEntMan.SpawnEntity("WeaponPistolViper", new EntityCoordinates(deck, new Vector2(4.5f, 3.5f)));
            Assert.That(HandSys.TryPickupAnyHand(SPlayer, gun), Is.True, "Precondition: the player holds a gun.");
        });

        if (!commanded)
        {
            await WaitUntil(() => conduct.IsWarned(deck, "conduct", SPlayer), 60,
                () => "A ship with no command still tells a player to put his gun away.");
        }
        else
        {
            var ticks = (int) (WGCrewConductSystem.BrandishTime.TotalSeconds + 3) * 30;
            for (var waited = 0; waited < ticks; waited += 30)
            {
                await RunTicks(30);
                await Server.WaitAssertion(() =>
                    Assert.That(conduct.IsWarned(deck, "conduct", SPlayer), Is.False, "The crew don't tell the owner to put his gun away."));
            }

            await Server.WaitAssertion(() =>
            {
                var memories = SEntMan.TryGetComponent<NPCRetaliationComponent>(crew, out var retaliation)
                    ? retaliation.AttackMemories
                    : null;
                var remembered = memories?.ContainsKey(SPlayer) == true;
                Assert.That(remembered, Is.False, "Nor do they turn on him.");
            });
        }

        await Server.WaitPost(() =>
        {
            SEntMan.DeleteEntity(gun);
            Transform.SetCoordinates(SPlayer, new EntityCoordinates(MapData.MapUid, Vector2.Zero));
        });
        await RunTicks(5);
    }
}
