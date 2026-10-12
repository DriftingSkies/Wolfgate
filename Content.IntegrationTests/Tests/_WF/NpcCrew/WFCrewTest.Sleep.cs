#nullable enable
using System.Numerics;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.NpcCrew;

public sealed partial class WFCrewTest
{
    /// <summary>An idle crewman in safe air may sleep; an alert, bad air or leaving the ship keeps him awake.</summary>
    [Test]
    public async Task IdleCrewMaySleepUntilTheyHaveSomethingToDo()
    {
        await AddAtmosphere();
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var vacuum = await CreateDeck(new Vector2(20, 0), 5, gravity: true);
        var attacker = await CreateDeck(new Vector2(40, 0), 3, gravity: true);
        await Server.WaitAssertion(() =>
        {
            FillCrewTestAir(deck);
            var crew = Server.System<WFCrewSystem>();
            var sleep = Server.System<WFCrewSleepSystem>();
            var idle = crew.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(2.5f)), "sleep")!.Value;
            var airless = crew.SpawnCrewman(WFCrewRoles.Deckhand, new EntityCoordinates(vacuum, new Vector2(2.5f)), "sleep")!.Value;
            SEntMan.GetComponent<HTNComponent>(idle).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(airless).Enabled = false;

            Assert.That(sleep.StaysAwake(idle), Is.False, "Nothing to do in safe air.");
            Assert.That(sleep.StaysAwake(airless), Is.False, "A crewman kitted out for a post in vacuum may rest there.");
            SEntMan.RemoveComponent<WFCrewVoidKitComponent>(airless);
            Assert.That(sleep.StaysAwake(airless), Is.True, "Vacuum has to be walked out of.");

            Server.System<WFCrewAlertSystem>().ReportShipThreat(deck, "sleep", attacker);
            Assert.That(sleep.StaysAwake(idle), Is.True, "An alerted ship keeps its crew awake.");
        });
    }
}
