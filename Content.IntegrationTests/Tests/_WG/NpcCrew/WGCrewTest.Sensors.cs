#nullable enable
using System.Numerics;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Medical.SuitSensors;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>A crewman's suit sensors are off from the moment he spawns, so the crew monitor doesn't list him.</summary>
    [Test]
    public async Task CrewSpawnWithTheirSuitSensorsOff()
    {
        var ship = await CreateDeck(new Vector2(40, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand, new EntityCoordinates(ship, new Vector2(2.5f)), "sensors")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;

            var sensors = 0;
            foreach (var worn in Server.System<InventorySystem>().GetHandOrInventoryEntities(crew))
            {
                if (!SEntMan.TryGetComponent<SuitSensorComponent>(worn, out var sensor))
                    continue;
                sensors++;
                Assert.That(sensor.Mode, Is.EqualTo(SuitSensorMode.SensorOff), $"{SEntMan.ToPrettyString(worn)} reports to the crew monitor.");
            }
            Assert.That(sensors, Is.GreaterThan(0), "The deckhand's kit carries suit sensors to switch off.");
            SEntMan.DeleteEntity(crew);
        });
    }
}
