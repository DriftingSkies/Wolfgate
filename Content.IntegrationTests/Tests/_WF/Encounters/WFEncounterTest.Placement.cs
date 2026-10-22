#nullable enable
using System.Numerics;
using Content.Server._WF.Encounters.Systems;
using Content.Shared._WF.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string PlacementPrototypes = @"
- type: wfEncounter
  id: WFTestPlacementPatrol
  name: wf-encounter-name-patrol
  start: Manual
  avoidStations: [ Caelestinus ]
  placement: Station
  standoff: 3000
  ships:
  - key: lead
    vessel: WFDredger
";

    /// <summary>A patrol posted off a station beside an avoided one is never placed near the avoided station.</summary>
    [Test]
    public async Task StationPatrolKeepsOffAnAvoidedNeighbour()
    {
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var transform = Server.System<SharedTransformSystem>();
            maps.CreateMap(out var map);
            var central = Port(map, Vector2.Zero, "Caelestinus Central");
            Port(map, new Vector2(2000, 0), "Halcyon");

            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestPlacementPatrol");
            var scheduler = Server.System<WFEncounterSchedulerSystem>();
            // The standoff is kept from the avoided station's hull, not its centre.
            var hull = SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(central).LocalAABB;
            var keepOff = hull.Size.Length() / 2f + prototype.Standoff;
            var hullCentre = Vector2.Transform(hull.Center, transform.GetWorldMatrix(central));
            for (var i = 0; i < 40; i++)
            {
                Assert.That(scheduler.TryPlaceAtStations(prototype, map, out var origin, out var stops), Is.True);
                Assert.That(stops, Does.Not.Contain(central));
                Assert.That(Vector2.Distance(origin.Position, hullCentre), Is.GreaterThanOrEqualTo(keepOff),
                    "The patrol is posted at least its standoff from the avoided station.");
            }

            maps.DeleteMap(map);
        });
        await RunTicks(5);

        EntityUid Port(MapId map, Vector2 at, string name)
        {
            var grid = Server.System<SharedMapSystem>().CreateGridEntity(map);
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            Server.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, at);
            SEntMan.System<MetaDataSystem>().SetEntityName(grid.Owner, name);
            SEntMan.SpawnEntity("AirlockShuttle", new EntityCoordinates(grid.Owner, new Vector2(0.5f)));
            return grid.Owner;
        }
    }
}
