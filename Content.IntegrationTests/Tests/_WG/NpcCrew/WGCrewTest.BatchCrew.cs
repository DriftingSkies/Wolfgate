#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.Components;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.SSDIndicator;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>
    /// A blow to one crewman turns the rest of the crew on the attacker, hands who only fight when attacked among them;
    /// the helm stays manned and a coward takes shelter, and runs when the stranger comes near.
    /// </summary>
    [Test]
    public async Task AttackOnOneCrewmanIsAnsweredByTheCrewsFighters()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 9, gravity: true);
        EntityUid cowardUid = default, visitorUid = default;
        await Server.WaitAssertion(() =>
        {
            // A coward only runs where there is air to run to.
            FillCrewTestAir(deck);
            var crewSystem = Server.System<WGCrewSystem>();
            var struck = ConvoyCrew(deck, "WGCrewDeckhand", "batch-crew", new Vector2(1.5f));
            var bystander = ConvoyCrew(deck, "WGCrewDeckhand", "batch-crew", new Vector2(2.5f, 1.5f));
            var coward = ConvoyCrew(deck, "WGCrewDeckhand", "batch-crew", new Vector2(3.5f, 1.5f));
            var officer = ConvoyCrew(deck, "WGCrewRadioOperator", "batch-crew", new Vector2(6.5f));
            var pilot = ConvoyCrew(deck, "WGCrewPilot", "batch-crew", new Vector2(7.5f));
            crewSystem.SetEngagement(struck, WGCrewEngagement.WhenAttacked);
            crewSystem.SetEngagement(bystander, WGCrewEngagement.WhenAttacked);
            crewSystem.SetEngagement(coward, WGCrewEngagement.Never);
            var visitor = ConvoyCrew(deck, "WGCrewDeckhand", "batch-visitor", new Vector2(1.5f, 3.5f));
            Server.System<NpcFactionSystem>().ClearFactions(visitor);

            var punch = new DamageSpecifier { DamageDict = { ["Blunt"] = 5 } };
            Server.System<DamageableSystem>().TryChangeDamage(struck, punch, ignoreResistances: true, origin: visitor);

            Assert.That(Memories(struck).ContainsKey(visitor), Is.True, "The struck hand answers for himself.");
            Assert.That(SEntMan.GetComponent<WGCrewComponent>(struck).Struck.ContainsKey(visitor), Is.True,
                "The blow is his own, so he may go after the attacker unseen.");
            Assert.That(Memories(officer).ContainsKey(visitor), Is.True, "The radio officer takes on whoever attacks his crew.");
            Assert.That(Server.System<NpcFactionSystem>().GetHostiles(officer), Does.Contain(visitor));
            Assert.That(SEntMan.GetComponent<WGCrewComponent>(officer).Struck.ContainsKey(visitor), Is.False,
                "A crewmate's blow does not send him after someone unseen.");
            Assert.That(Memories(bystander).ContainsKey(visitor), Is.True, "Another hand stands by his crewmate.");
            Assert.That(Memories(pilot).ContainsKey(visitor), Is.False, "The helm stays manned.");
            Assert.That(Server.System<WGCrewShelterSystem>().IsSheltering(coward), Is.True, "A coward takes shelter.");
            cowardUid = coward;
            visitorUid = visitor;
            SEntMan.RemoveComponent<Content.Server.NPC.HTN.HTNComponent>(visitor);
            Server.System<SharedTransformSystem>().SetCoordinates(visitor, new EntityCoordinates(deck, new Vector2(4.5f, 1.5f)));
        });
        await RunTicks(Ticks(3));
        await Server.WaitAssertion(() =>
        {
            var post = SEntMan.GetComponent<WGCrewComponent>(cowardUid).Post;
            Assert.That(post, Is.Not.Null);
            var from = SEntMan.GetComponent<TransformComponent>(visitorUid).LocalPosition;
            Assert.That((post!.Value.Position - from).Length(), Is.GreaterThan(3f), "With a stranger beside him the coward runs for somewhere far from him.");
        });

        Dictionary<EntityUid, TimeSpan> Memories(EntityUid uid) => SEntMan.GetComponent<NPCRetaliationComponent>(uid).AttackMemories;
    }

    /// <summary>A stranger a Warn crew noticed aboard, who stays past his time, is taken on by the crew's fighters.</summary>
    [Test]
    public async Task WarnedStrangerWhoStaysIsTakenOn()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        EntityUid officer = default, visitor = default;
        await Server.WaitAssertion(() =>
        {
            officer = ConvoyCrew(deck, "WGCrewRadioOperator", "batch-warn", new Vector2(1.5f));
            visitor = ConvoyCrew(deck, "WGCrewDeckhand", "batch-stranger", new Vector2(3.5f, 1.5f));
            Server.System<NpcFactionSystem>().ClearFactions(visitor);
            var rules = SEntMan.EnsureComponent<WGCrewSecurityComponent>(officer);
            rules.Boarding = WGCrewSecurityResponse.Warn;
            rules.WarnTime = TimeSpan.FromSeconds(1);
            Assert.That(Server.System<WGCrewWeaponSystem>().CanSee(officer, visitor), Is.True, "Precondition: the stranger is in sight.");
        });
        await RunTicks(Ticks(5));
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewSecuritySystem>().OverstayedWarning(deck, "batch-warn", visitor), Is.True,
                "The stranger stayed past his time.");
            var memories = SEntMan.GetComponent<NPCRetaliationComponent>(officer).AttackMemories;
            Assert.That(memories.ContainsKey(visitor), Is.True, "The officer takes him on as an attacker.");
            Assert.That(Server.System<NpcFactionSystem>().GetHostiles(officer), Does.Contain(visitor));
        });
    }

    /// <summary>
    /// Crew never show the SSD icon, and a dead boarder no longer makes a crew row aboard the ship he boarded, while a
    /// crew with nobody left alive is still listed.
    /// </summary>
    [Test]
    public async Task DeadBoarderNoLongerCountsAboardThePrey()
    {
        var raider = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var prey = await CreateDeck(new Vector2(6, 12), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var hand = ConvoyCrew(raider, "WGCrewDeckhand", "batch-raid", new Vector2(1.5f));
            var boarder = ConvoyCrew(prey, "WGCrewMarine", "batch-raid", new Vector2(2.5f));
            var lost = ConvoyCrew(prey, "WGCrewDeckhand", "batch-lost", new Vector2(3.5f));
            // The radar link is drawn between the rows of one battlegroup.
            SEntMan.GetComponent<WGCrewComponent>(hand).Battlegroup = "batch-wing";
            SEntMan.GetComponent<WGCrewComponent>(boarder).Battlegroup = "batch-wing";
            foreach (var member in new[] { hand, boarder, lost })
                Assert.That(SEntMan.HasComponent<SSDIndicatorComponent>(member), Is.False, "NPC crew never show the SSD icon.");

            var objectives = Server.System<WGCrewObjectiveSystem>();
            Assert.That(objectives.Snapshot().Count(row => row.Group == "batch-raid"), Is.EqualTo(2),
                "Precondition: a living boarder makes a row aboard the prey.");

            var mobs = Server.System<MobStateSystem>();
            mobs.ChangeMobState(boarder, MobState.Dead);
            mobs.ChangeMobState(lost, MobState.Dead);
            var rows = objectives.Snapshot();
            Assert.That(rows.Where(row => row.Group == "batch-raid").Select(row => SEntMan.GetEntity(row.Grid)),
                Is.EquivalentTo(new[] { raider }), "A dead boarder is no crew of the prey.");
            var lostRow = rows.Single(row => row.Group == "batch-lost");
            Assert.That(lostRow.Alive, Is.Zero);
            Assert.That(lostRow.Members, Is.EqualTo(1), "A crew with nobody left alive is still listed.");
        });
    }
}
