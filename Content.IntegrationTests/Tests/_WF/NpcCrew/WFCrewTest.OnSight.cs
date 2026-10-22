#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>
    /// An on-sight crew fires on any faction enemy it sees: hands anywhere, the helm and guns only at one aboard. A spared
    /// company is left alone, and a crew without the mark keeps to boarders and attackers.
    /// </summary>
    [Test]
    public async Task OnSightCrewEngageEnemiesTheySee()
    {
        var home = await CreateDeck(new Vector2(40, 0), 5, gravity: true);
        var near = await CreateDeck(new Vector2(40, 7), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var weapons = Server.System<WFCrewWeaponSystem>();
            var hand = ConvoyCrew(home, "WFCrewDeckhand", "reavers", new Vector2(1.5f, 4.5f));
            var gunner = ConvoyCrew(home, "WFCrewGunner", "reavers", new Vector2(2.5f, 4.5f));
            var outside = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(near, new Vector2(1.5f, 0.5f)));
            var aboard = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(home, new Vector2(2.5f, 2.5f)));

            Assert.That(weapons.CanSee(hand, outside), Is.True, "Precondition: the one outside is in sight.");
            Assert.That(weapons.CanEngage(hand, outside), Is.False, "An ordinary crew starts nothing off its ship.");
            Assert.That(weapons.CanEngage(gunner, aboard), Is.False, "An ordinary gunner keeps to his guns.");

            var onSight = SEntMan.EnsureComponent<WFCrewOnSightComponent>(home);
            onSight.Spares = new List<string> { "WFUnderground" };
            Assert.That(weapons.CanEngage(hand, outside), Is.True, "A hand fires on an enemy he sees off the ship.");
            Assert.That(weapons.CanEngage(gunner, aboard), Is.True, "The gunner fires on an enemy aboard.");
            Assert.That(weapons.CanEngage(gunner, outside), Is.False, "But not on one outside: the guns stay manned.");

            SEntMan.EnsureComponent<CompanyComponent>(outside).CompanyName = "WFUnderground";
            Assert.That(weapons.CanEngage(hand, outside), Is.False, "A spared company is left alone.");
        });
    }

    /// <summary>The Reavers' crews take every navy's people for enemies, the Dynasty's included, and the Underground for allies.</summary>
    [Test]
    public async Task ReaverCrewsSpareOnlyTheUnderground()
    {
        var deck = await CreateDeck(new Vector2(60, 0), 7, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var factions = Server.System<NpcFactionSystem>();
            var weapons = Server.System<WFCrewWeaponSystem>();
            var reaver = ConvoyCrew(deck, "WFCrewDeckhand", "reavers", new Vector2(1.5f));
            factions.ClearFactions(reaver);
            factions.AddFaction(reaver, "WFAshfallReavers");

            EntityUid Person(float x, params string[] memberOf)
            {
                var mob = SEntMan.SpawnAtPosition("MobHuman", new EntityCoordinates(deck, new Vector2(x, 3.5f)));
                factions.ClearFactions(mob);
                foreach (var faction in memberOf)
                    factions.AddFaction(mob, faction);
                return mob;
            }

            var colonist = Person(1.5f, "NanoTrasen");
            var dynasty = Person(2.5f, "PirateNF");
            var fugitive = Person(3.5f, "NanoTrasen", "WFUnderground");
            var hostiles = factions.GetNearbyHostiles(reaver, 10f).ToList();
            Assert.That(hostiles, Does.Contain(colonist), "A colonist is an enemy.");
            Assert.That(hostiles, Does.Contain(dynasty), "So is the Dynasty.");
            Assert.That(hostiles, Does.Not.Contain(fugitive), "The Underground are allies.");
            Assert.That(weapons.HasLiveThreat(reaver), Is.True);
        });
    }
}
