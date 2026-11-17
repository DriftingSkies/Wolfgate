#nullable enable
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
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
            var crew = Server.System<WGCrewSystem>();
            var sleep = Server.System<WGCrewSleepSystem>();
            var idle = crew.SpawnCrewman(WGCrewRoles.Deckhand, new EntityCoordinates(deck, new Vector2(2.5f)), "sleep")!.Value;
            var airless = crew.SpawnCrewman(WGCrewRoles.Deckhand, new EntityCoordinates(vacuum, new Vector2(2.5f)), "sleep")!.Value;
            SEntMan.GetComponent<HTNComponent>(idle).Enabled = false;
            SEntMan.GetComponent<HTNComponent>(airless).Enabled = false;

            Assert.That(sleep.StaysAwake(idle), Is.False, "Nothing to do in safe air.");
            Assert.That(sleep.StaysAwake(airless), Is.False, "A crewman kitted out for a post in vacuum may rest there.");
            SEntMan.RemoveComponent<WGCrewVoidKitComponent>(airless);
            Assert.That(sleep.StaysAwake(airless), Is.True, "Vacuum has to be walked out of.");

            Server.System<WGCrewAlertSystem>().ReportShipThreat(deck, "sleep", attacker);
            Assert.That(sleep.StaysAwake(idle), Is.True, "An alerted ship keeps its crew awake.");
        });
    }
}
