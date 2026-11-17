#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WG.SectorControl;
using Content.Server._WG.DroneRings;
using Content.Server._WG.SectorControl.Systems;
using Content.Shared._WG.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.DroneRings;

/// <summary>Drone spawners are skipped in held sector cells; other debris is not.</summary>
[TestOf(typeof(WGDroneHeldSpaceSystem))]
public sealed class WGDroneHeldSpaceTest : WGSectorTestBase
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGDroneHeldTestSpawner

- type: entity
  id: WGDroneHeldTestRock

- type: wgDroneSources
  id: WGDroneHeldTest
  spawners:
  - WGDroneHeldTestSpawner
";

    private static readonly WGSectorCell Held = new(4, -2);
    private static readonly WGSectorCell Open = new(5, -2);

    [TearDown]
    public async Task ResetClearing()
    {
        await Server.WaitPost(() =>
        {
            Server.ResolveDependency<IConfigurationManager>().SetCVar(SectorControlCVars.ClearingMinutes, SectorControlCVars.ClearingMinutes.DefaultValue);
            Server.System<WGSectorTerritorySystem>().Clear(MapData.MapId);
        });
    }

    /// <summary>A drone spawner in a held or clearing cell is cancelled; one in open space or after the clearing, and other debris in a held cell, are not.</summary>
    [Test]
    public async Task DronesAreCancelledOnlyInHeldCells()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WGSectorTerritorySystem>();
            var map = MapData.MapId;
            Assert.That(territory.TryClaim(map, Held, Holder, null), Is.True);

            Assert.That(Placing("WGDroneHeldTestSpawner", map, CentreOf(Held)), Is.True, "A drone in held space.");
            Assert.That(Placing("WGDroneHeldTestSpawner", map, CentreOf(Open)), Is.False, "A drone in open space.");
            Assert.That(Placing("WGDroneHeldTestRock", map, CentreOf(Held)), Is.False, "Other debris in held space.");

            Server.ResolveDependency<IConfigurationManager>().SetCVar(SectorControlCVars.ClearingMinutes, 0.05f);
            Assert.That(territory.Free(map, Held), Is.True);
            Assert.That(Placing("WGDroneHeldTestSpawner", map, CentreOf(Held)), Is.True, "Still clearing once freed.");
            Assert.That(Placing("WGDroneHeldTestRock", map, CentreOf(Held)), Is.False, "Other debris in a clearing cell.");
        });

        await RunTicks(200);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Placing("WGDroneHeldTestSpawner", MapData.MapId, CentreOf(Held)), Is.False, "Once the clearing is over.");
        });
    }

    /// <summary>Raises the placing event and returns whether it was cancelled.</summary>
    private bool Placing(string proto, MapId map, System.Numerics.Vector2 at)
    {
        var ev = new WGDebrisPlacingEvent(proto, new MapCoordinates(at, map));
        SEntMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return ev.Cancelled;
    }
}
