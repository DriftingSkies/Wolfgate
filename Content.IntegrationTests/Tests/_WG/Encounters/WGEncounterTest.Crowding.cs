#nullable enable
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Encounters;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Encounters;

public sealed partial class WGEncounterTest
{
    /// <summary>A spot is crowded once enough fighting encounters have a ship near it; freight and resolved ones don't count.</summary>
    [Test]
    public async Task CrowdingCountsNearbyEncountersAndFights()
    {
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var scheduler = Server.System<WGEncounterSchedulerSystem>();
            var config = Server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(EncountersCVars.CrowdRadius, 5000f);
            config.SetCVar(EncountersCVars.CrowdMax, 2);
            maps.CreateMap(out var map);
            var spot = new MapCoordinates(Vector2.Zero, map);
            var fightsBefore = scheduler.Fights();

            var first = Encounter(map, new Vector2(1000, 0), engaged: true);
            Assert.That(scheduler.Crowded(spot), Is.False, "One encounter nearby leaves room.");

            var far = Encounter(map, new Vector2(9000, 0), engaged: false);
            Assert.That(scheduler.Crowded(spot), Is.False, "An encounter past the radius doesn't count.");

            var freight = Encounter(map, new Vector2(0, 1500), engaged: false);
            SEntMan.GetComponent<WGEncounterComponent>(freight).Freight = true;
            Assert.That(scheduler.Crowded(spot), Is.False, "Freight doesn't count.");

            var second = Encounter(map, new Vector2(-2000, 0), engaged: true);
            Assert.That(scheduler.Crowded(spot), Is.True, "Two encounters nearby crowd the spot.");
            Assert.That(scheduler.Fights() - fightsBefore, Is.EqualTo(2));

            SEntMan.GetComponent<WGEncounterComponent>(second).Resolution = WGEncounterResolution.Expired;
            Assert.That(scheduler.Crowded(spot), Is.False, "A resolved encounter no longer counts.");
            Assert.That(scheduler.Fights() - fightsBefore, Is.EqualTo(1));

            config.SetCVar(EncountersCVars.CrowdRadius, 0f);
            SEntMan.GetComponent<WGEncounterComponent>(second).Resolution = null;
            Assert.That(scheduler.Crowded(spot), Is.False, "A radius of 0 turns the check off.");
            config.SetCVar(EncountersCVars.CrowdRadius, EncountersCVars.CrowdRadius.DefaultValue);

            SEntMan.DeleteEntity(first);
            SEntMan.DeleteEntity(second);
            SEntMan.DeleteEntity(far);
            SEntMan.DeleteEntity(freight);
            maps.DeleteMap(map);
        });
        await RunTicks(5);

        EntityUid Encounter(MapId map, Vector2 at, bool engaged)
        {
            var ship = SEntMan.SpawnEntity(null, new MapCoordinates(at, map));
            var uid = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var encounter = SEntMan.AddComponent<WGEncounterComponent>(uid);
            var state = new WGEncounterShipState { Grid = ship };
            if (engaged)
                state.Engaged.Add(ship);
            encounter.Ships["lead"] = state;
            return uid;
        }
    }
}
