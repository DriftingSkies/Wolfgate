using System.Numerics;
using Content.Server._WF.Encounters.Systems;
using Content.Shared._WF.Vehicles.Core;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>A vehicle's interior map is no open space: encounters never spawn inside it.</summary>
[TestFixture]
[TestOf(typeof(WFEncounterSchedulerSystem))]
public sealed class WFEncounterVehicleInteriorTest
{
    [Test]
    public async Task VehicleInteriorIsNotOpenSpaceTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var scheduler = server.EntMan.System<WFEncounterSchedulerSystem>();
            var far = new Vector2(60000f, -60000f);
            Assert.That(scheduler.IsClearSpace(map.MapId, far, 100f, 0f), Is.True, "a plain map is not clear space");

            server.EntMan.AddComponent<VehicleInteriorLinkComponent>(map.MapUid);
            Assert.That(scheduler.IsClearSpace(map.MapId, far, 100f, 0f), Is.False, "a vehicle interior counts as open space");
        });

        await pair.CleanReturnAsync();
    }
}
