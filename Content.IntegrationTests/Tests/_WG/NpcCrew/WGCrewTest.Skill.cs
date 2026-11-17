#nullable enable
using System;
using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Each skill level is no worse than the one below it, and a mission's skill reaches every crewman.</summary>
    [Test]
    public async Task CrewSkillScalesAndReachesTheCrew()
    {
        var levels = Enum.GetValues<WGCrewSkill>().Select(WGCrewSkills.Of).ToArray();
        for (var i = 1; i < levels.Length; i++)
        {
            Assert.That(levels[i].AimError, Is.LessThanOrEqualTo(levels[i - 1].AimError));
            Assert.That(levels[i].ShootDelay, Is.LessThanOrEqualTo(levels[i - 1].ShootDelay));
            Assert.That(levels[i].GunneryError, Is.LessThanOrEqualTo(levels[i - 1].GunneryError));
            Assert.That(levels[i].Leading, Is.GreaterThanOrEqualTo(levels[i - 1].Leading));
            Assert.That(levels[i].Handling, Is.GreaterThanOrEqualTo(levels[i - 1].Handling));
            Assert.That(levels[i].Evasion, Is.GreaterThanOrEqualTo(levels[i - 1].Evasion));
            Assert.That(levels[i].DamageTaken, Is.LessThanOrEqualTo(levels[i - 1].DamageTaken));
            Assert.That(levels[i].DamageDealt, Is.GreaterThanOrEqualTo(levels[i - 1].DamageDealt));
        }

        Assert.That(WGCrewSkills.Of(WGCrewSkill.Green).DodgesFire, Is.False);
        Assert.That(WGCrewSkills.Of(WGCrewSkill.Elite).AimError, Is.Zero);

        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var hand = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new Robust.Shared.Map.EntityCoordinates(deck, new Vector2(2.5f)), "skill")!.Value;
            SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(hand).Enabled = false;
            Assert.That(SEntMan.GetComponent<WGCrewComponent>(hand).Skill, Is.EqualTo(WGCrewSkill.Veteran));
            Server.System<WGCrewSetupSystem>().ApplyMission(hand, deck, new WGCrewMission { Group = "skill", Skill = WGCrewSkill.Green });
            Assert.That(SEntMan.GetComponent<WGCrewComponent>(hand).Skill, Is.EqualTo(WGCrewSkill.Green));
        });
    }

    /// <summary>The same hit hurts a green crewman more than an elite one.</summary>
    [Test]
    public async Task GreenCrewTakeMoreDamageThanElite()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = Server.System<WGCrewSystem>();
            var damageable = Server.System<Content.Shared.Damage.DamageableSystem>();
            Content.Shared.FixedPoint.FixedPoint2 Hit(WGCrewSkill skill, float x)
            {
                var hand = crew.SpawnCrewman(WGCrewRoles.Deckhand, new Robust.Shared.Map.EntityCoordinates(deck, new Vector2(x, 2.5f)), "frail")!.Value;
                SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(hand).Enabled = false;
                SEntMan.GetComponent<WGCrewComponent>(hand).Skill = skill;
                var before = SEntMan.GetComponent<Content.Shared.Damage.DamageableComponent>(hand).TotalDamage;
                var blow = new Content.Shared.Damage.DamageSpecifier { DamageDict = { ["Blunt"] = 10 } };
                damageable.TryChangeDamage(hand, blow, ignoreResistances: true);
                return SEntMan.GetComponent<Content.Shared.Damage.DamageableComponent>(hand).TotalDamage - before;
            }

            var elite = Hit(WGCrewSkill.Elite, 1.5f);
            var green = Hit(WGCrewSkill.Green, 3.5f);
            Assert.That(elite, Is.GreaterThan(Content.Shared.FixedPoint.FixedPoint2.Zero), "The blow has to land at all.");
            Assert.That(green, Is.GreaterThan(elite), $"green took {green}, elite took {elite}");
        });
    }
}
