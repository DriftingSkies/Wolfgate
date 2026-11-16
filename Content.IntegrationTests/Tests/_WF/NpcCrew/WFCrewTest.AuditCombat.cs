#nullable enable
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>A crewman off his post's grid starts no fight, but answers whoever attacked him, in sight.</summary>
    [Test]
    public async Task OffPostCrewFightBackOnlyAgainstTheirAttacker()
    {
        var home = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var away = await CreateDeck(new Vector2(6, 12), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = ConvoyCrew(home, "WFCrewDeckhand", "off-post", new Vector2(1.5f));
            Server.System<SharedTransformSystem>().SetCoordinates(crew, new EntityCoordinates(away, new Vector2(1.5f)));
            var attacker = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(away, new Vector2(3.5f)));
            var bystander = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(away, new Vector2(3.5f, 1.5f)));
            var weapons = Server.System<WFCrewWeaponSystem>();
            Assert.That(weapons.CanSee(crew, attacker), Is.True, "Precondition: the attacker is in sight.");
            Assert.That(weapons.CanEngage(crew, attacker), Is.False, "Off his post he starts nothing.");

            var retaliation = SEntMan.GetComponent<NPCRetaliationComponent>(crew);
            Assert.That(Server.System<NPCRetaliationSystem>().TryRetaliate((crew, retaliation), attacker), Is.True);
            Assert.That(weapons.CanEngage(crew, attacker), Is.True, "He answers the one who attacked him.");
            Assert.That(weapons.CanEngage(crew, bystander), Is.False, "Only that one.");
        });
    }

    /// <summary>Hitscan damage names its gun as the origin; crew still count whoever holds the gun as the attacker.</summary>
    [Test]
    public async Task HitscanAttackersAreRegisteredThroughTheirGun()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = ConvoyCrew(deck, "WFCrewDeckhand", "hitscan", new Vector2(1.5f));
            var attacker = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(3.5f)));
            var gun = SEntMan.SpawnAtPosition("WeaponPistolMk58", new EntityCoordinates(deck, new Vector2(3.5f, 1.5f)));
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(attacker, gun, checkActionBlocker: false), Is.True);
            var crews = Server.System<WFCrewSystem>();
            Assert.That(crews.Wielder(gun), Is.EqualTo(attacker));
            Assert.That(crews.Wielder(attacker), Is.EqualTo(attacker));

            var beam = new DamageSpecifier { DamageDict = { ["Heat"] = 5 } };
            Server.System<DamageableSystem>().TryChangeDamage(crew, beam, ignoreResistances: true, origin: gun);
            var memories = SEntMan.GetComponent<NPCRetaliationComponent>(crew).AttackMemories;
            Assert.That(memories.ContainsKey(attacker), Is.True, "Whoever held the gun is the attacker.");
            Assert.That(memories.ContainsKey(gun), Is.False);
        });
    }

    /// <summary>
    /// Asking whether crew may fight claims nothing; the hunt for an unseen hostile takes as many as the opener's skill
    /// allows (one for a regular, two for a veteran) and the rest hold.
    /// </summary>
    [Test]
    public async Task UnseenHostileIsHuntedByAPartySizedBySkill()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 25, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var first = ConvoyCrew(deck, "WFCrewDeckhand", "hunt", new Vector2(1.5f));
            var second = ConvoyCrew(deck, "WFCrewDeckhand", "hunt", new Vector2(2.5f, 1.5f));
            var third = ConvoyCrew(deck, "WFCrewDeckhand", "hunt", new Vector2(3.5f, 1.5f));
            var hostile = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(22.5f)));
            var heard = STiming.CurTime + TimeSpan.FromSeconds(30);
            foreach (var member in new[] { first, second, third })
                SEntMan.GetComponent<WFCrewComponent>(member).RadioSightings[hostile] = heard;

            var weapons = Server.System<WFCrewWeaponSystem>();
            Assert.That(weapons.CanSee(first, hostile), Is.False, "Precondition: nobody sees the hostile.");
            Assert.That(weapons.HasLiveThreat(second), Is.True, "Precondition: a radio contact is a threat.");
            Assert.That(weapons.CanEngage(first, hostile), Is.True);
            Assert.That(weapons.CanEngage(third, hostile), Is.True, "Asking claims nothing.");

            weapons.ClaimAdvance(first, hostile);
            Assert.That(weapons.CanEngage(first, hostile), Is.True, "The claimant goes looking.");
            Assert.That(weapons.CanEngage(second, hostile), Is.True, "A veteran hunt has room for two.");
            weapons.ClaimAdvance(second, hostile);
            Assert.That(weapons.CanEngage(second, hostile), Is.True, "The second claimant goes looking.");
            Assert.That(weapons.CanEngage(third, hostile), Is.False, "The rest hold.");

            var other = SEntMan.SpawnAtPosition(Hostile, new EntityCoordinates(deck, new Vector2(22.5f, 20.5f)));
            foreach (var member in new[] { first, second, third })
            {
                var crew = SEntMan.GetComponent<WFCrewComponent>(member);
                crew.Skill = WFCrewSkill.Regular;
                crew.RadioSightings[other] = heard;
            }

            weapons.ClaimAdvance(third, other);
            Assert.That(weapons.CanEngage(third, other), Is.True, "A regular opens a hunt of one.");
            Assert.That(weapons.CanEngage(first, other), Is.False, "The rest hold.");
        });
    }
}
