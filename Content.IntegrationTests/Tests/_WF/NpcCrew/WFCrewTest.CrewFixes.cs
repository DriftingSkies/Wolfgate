#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>
    /// A cargo job that fails does not block the order: its target is skipped, the next one is picked, and only
    /// when every target left has failed does the order report work-blocked until it is cancelled.
    /// </summary>
    [TestCase(1)]
    [TestCase(2)]
    public async Task CrewSkipsFailedWorkTarget(int sheets)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var source = await CreateDeck(new Vector2(24, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var work = Server.System<WFCrewWorkSystem>();
            var hands = Server.System<SharedHandsSystem>();
            var xforms = Server.System<SharedTransformSystem>();
            var home = new EntityCoordinates(deck, new Vector2(2.5f));
            var crew = Server.System<WFCrewSystem>().SpawnCrewman(WFCrewRoles.Deckhand, home, "skip")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            for (var i = 0; i < sheets; i++)
                SEntMan.SpawnAtPosition("SheetSteel1", new EntityCoordinates(source, new Vector2(1.5f + i, 2.5f)));

            Assert.That(work.Advance(deck, "skip", WFCrewObjectiveKind.Salvage, source), Is.EqualTo("working"));
            Assert.That(work.Destination(crew, out var at), Is.True);
            xforms.SetCoordinates(crew, at);
            work.Perform(crew);
            var first = hands.EnumerateHeld(crew).Single(item => SEntMan.HasComponent<StackComponent>(item));

            // The cargo is lost on the way home, so the delivery fails.
            Assert.That(hands.TryDrop(crew, first), Is.True);
            xforms.SetCoordinates(crew, home);
            work.Perform(crew);

            var next = work.Advance(deck, "skip", WFCrewObjectiveKind.Salvage, source);
            if (sheets == 1)
            {
                Assert.That(next, Is.EqualTo("work-blocked"), "The only target has failed.");
                work.Cancel(deck, "skip");
                Assert.That(work.Advance(deck, "skip", WFCrewObjectiveKind.Salvage, source), Is.EqualTo("working"),
                    "Cancelling the order clears what it gave up on.");
                return;
            }

            Assert.That(next, Is.EqualTo("working"), "The failed sheet must not block the other one.");
            Assert.That(work.Destination(crew, out var other), Is.True);
            xforms.SetCoordinates(crew, other);
            work.Perform(crew);
            Assert.That(hands.EnumerateHeld(crew).Single(item => SEntMan.HasComponent<StackComponent>(item)), Is.Not.EqualTo(first), "The next job takes the other sheet.");
        });
    }

    /// <summary>
    /// Crew spawned on a ship set its access up and lose their key when they die (back if revived) or are deleted;
    /// other grids stay unmanaged.
    /// </summary>
    [Test]
    public async Task CrewAccessSetsUpShipsOnly()
    {
        var other = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var ship = await CreateDeck(new Vector2(20, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crewSystem = Server.System<WFCrewSystem>();
            SEntMan.EnsureComponent<Content.Shared.Station.Components.StationMemberComponent>(other);
            var onOther = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(other, new Vector2(2.5f)), "access")!.Value;
            SEntMan.GetComponent<HTNComponent>(onOther).Enabled = false;
            Assert.That(SEntMan.HasComponent<WFShipAccessComponent>(other), Is.False,
                "A station grid keeps its own door access.");

            SEntMan.EnsureComponent<ShuttleComponent>(ship);
            var crew = crewSystem.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(ship, new Vector2(2.5f)), "access")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var access = SEntMan.GetComponent<WFShipAccessComponent>(ship);
            Assert.That(access.AllowList, Has.Count.EqualTo(1), "A ship is set up and its crew enrolled.");

            // A dead crewman's card opens nothing, so a looted card or a dragged body can't let anyone in.
            var mobState = Server.System<MobStateSystem>();
            mobState.ChangeMobState(crew, MobState.Dead);
            Assert.That(access.AllowList, Is.Empty, "A dead crewman's card loses its access.");
            mobState.ChangeMobState(crew, MobState.Alive);
            Assert.That(access.AllowList, Has.Count.EqualTo(1), "A revived crewman's card gets it back.");
            SEntMan.DeleteEntity(crew);
            Assert.That(access.AllowList, Is.Empty, "A deleted crewman's card is revoked.");
        });
    }

    /// <summary>A crew with a captain and a radio officer puts each line on the air once, by the officer.</summary>
    [Test]
    public async Task CrewRadioSpeaksThroughOneOperator()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 9, gravity: true);
        var raider = await CreateDeck(new Vector2(6, 12), 3, gravity: true);
        EntityUid officer = default, captain = default;
        await Server.WaitAssertion(() =>
        {
            officer = ConvoyCrew(deck, "WFCrewRadioOperator", "twin", new Vector2(2.5f));
            captain = ConvoyCrew(deck, "WFCrewCaptain", "twin", new Vector2(4.5f, 6.5f));
            var hit = new WFCrewHullHitEvent(deck, raider);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref hit, true);
            Assert.That(Sent(officer).Any(t => t.Line == WFRadioLine.Mayday), Is.True, "The officer reports the attack.");
            Assert.That(Sent(captain), Is.Empty, "The captain stays quiet while the officer is up.");

            Server.System<MobStateSystem>().ChangeMobState(officer, MobState.Dead);
            SEntMan.EventBus.RaiseLocalEvent(deck, ref hit, true);
            Assert.That(Sent(captain).Any(t => t.Line == WFRadioLine.Mayday), Is.True,
                "With the officer down the captain speaks.");
        });
    }
}
