#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.ShipCommand;
using Content.Server.Anomaly;
using Content.Server.Anomaly.Components;
using Content.Shared.Anomaly.Components;
using Content.Server.Carrying;
using Content.Server.Salvage.Magnet;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.ShipCommand;
using Content.IntegrationTests.Pair;
using Content.Shared.Doors.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._WG.AdminVessels;

/// <summary>The fixes from the Lupercal's second playtest (v0.30), checked on the first fixture's ship.</summary>
public sealed partial class LupercalPlaytestTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGTestAnomalyGenerator
  parent: MachineAnomalyGenerator
  components:
  - type: AnomalyGenerator
    spawnerPrototype: AnomalyIce
";

    private static async Task SecondPlaytest(TestPair pair, TestMapData map, EntityUid grid)
    {
        var server = pair.Server;
        var em = server.EntMan;
        // Ten seconds in all since the spawn: long enough for every door's auto-close and the crew's first rounds.
        await pair.RunTicksSync(240);

        await server.WaitAssertion(() =>
        {
            using var scope = Assert.EnterMultipleScope();
            var xforms = server.System<SharedTransformSystem>();
            var maps = server.System<SharedMapSystem>();
            var gridComp = em.GetComponent<MapGridComponent>(grid);

            // The secure windoors stand shut.
            foreach (var door in OnGrid<DoorComponent>(em, grid))
            {
                var proto = Proto(em, door.Owner) ?? string.Empty;
                if (proto.StartsWith("Windoor"))
                    Assert.That(door.Comp.State, Is.EqualTo(DoorState.Closed), $"{proto} at {em.GetComponent<TransformComponent>(door.Owner).LocalPosition} is {door.Comp.State}.");
            }

            // The magnet has offers, and faces space: the way it pulls leaves the ship within a few tiles.
            var station = server.System<StationSystem>().GetOwningStation(grid);
            Assert.That(station, Is.Not.Null);
            Assert.That(em.TryGetComponent<SalvageMagnetDataComponent>(station!.Value, out var data), Is.True, "The station keeps magnet offers.");
            Assert.That(data!.Offered, Is.Not.Empty, "The magnet has wrecks on offer.");
            var magnet = OnGrid<SalvageMagnetComponent>(em, grid).Single();
            var magnetXform = em.GetComponent<TransformComponent>(magnet.Owner);
            var pull = (magnetXform.LocalRotation - MathF.PI / 2).ToVec();
            var tile = maps.TileIndicesFor(grid, gridComp, magnetXform.Coordinates);
            var outside = Enumerable.Range(1, 4).Any(i =>
            {
                var at = tile + new Vector2i((int) MathF.Round(pull.X * i), (int) MathF.Round(pull.Y * i));
                return !maps.TryGetTileRef(grid, gridComp, at, out var tileRef) || tileRef.Tile.IsEmpty;
            });
            Assert.That(outside, Is.True, $"The magnet pulls toward {pull}, into the ship.");

            // Directional fans hold the air at the doors to space and the core's gates.
            Assert.That(OfProto(em, grid, "AtmosDeviceFanDirectional"), Has.Count.GreaterThanOrEqualTo(12));

            // Science has its anomaly generator, whose anomalies come in on the chamber's marker: that grid's own, never
            // one on another grid, and a grid with none falls back to a random tile.
            Assert.That(OfProto(em, grid, "MachineAnomalyGenerator"), Has.Count.EqualTo(1));
            var marker = OfProto(em, grid, "WGAnomalyChamberMarker").Single();
            em.SpawnEntity("WGAnomalyChamberMarker", map.GridCoords);
            var anomalies = server.System<AnomalySystem>();
            var before = OfProto(em, grid, "Paper").ToHashSet();
            Assert.That(anomalies.TrySpawnInChamber(grid, "Paper"), Is.True, "The Lupercal's chamber takes the anomaly.");
            var spawned = OfProto(em, grid, "Paper").Single(p => !before.Contains(p));
            Assert.That(em.GetComponent<TransformComponent>(spawned).Coordinates.Position,
                Is.EqualTo(em.GetComponent<TransformComponent>(marker).Coordinates.Position), "It lands on the Lupercal's marker.");
            var bare = server.System<SharedMapSystem>().CreateGridEntity(map.MapId);
            Assert.That(anomalies.TrySpawnInChamber(bare, "Paper"), Is.False, "A grid with no chamber falls back.");

            // The janitor's closet has its vendor; Medical has its defibrillators.
            Assert.That(OfProto(em, grid, "VendingMachineJaniDrobe"), Is.Not.Empty);
            Assert.That(OfProto(em, grid, "Defibrillator").Count + OfProto(em, grid, "DefibrillatorCompact").Count, Is.GreaterThanOrEqualTo(6));

            // The counter traders are the ship's own: the crew let them be in the locked-down docks.
            var command = server.System<WGShipCommandSystem>();
            var roster = em.GetComponent<WGShipRosterComponent>(grid);
            var mobs = server.System<MobStateSystem>();
            var marine = roster.Slots.Values.First(s => s.Role.Id == "WGCrewMarine" && s.Occupant is { } o && mobs.IsAlive(o)).Occupant!.Value;
            var shipCommand = (grid, em.GetComponent<WGShipCommandComponent>(grid));
            foreach (var counter in roster.Slots.Values.Where(s => s.Kind == WGShipRosterKind.Counter && s.Occupant != null))
                Assert.That(command.Welcome(shipCommand, marine, counter.Occupant!.Value, out _), Is.True, $"{counter.What} is welcome at its counter.");

            // A crewman's rounds stop at the ship's walls.
            var friendlyFire = server.System<WGCrewFriendlyFireSystem>();
            var wall = OfProto(em, grid, "WallPlastitaniumIndestructible").First();
            Assert.That(friendlyFire.Protected(marine, wall), Is.False, "A marine's bullet passes through a wall.");
        });

        // The generators finish a run: the Lupercal's anomaly appears on its chamber's marker, and a generator on a grid
        // with no chamber places its anomaly as it always has, not in the Lupercal's chamber.
        EntityUid chamberMarker = default, bareGrid = default;
        HashSet<EntityUid> anomaliesBefore = default!;
        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            chamberMarker = OfProto(em, grid, "WGAnomalyChamberMarker").Single();
            var bare = maps.CreateGridEntity(map.MapId);
            bareGrid = bare.Owner;
            server.System<SharedTransformSystem>().SetWorldPosition(bareGrid, new Vector2(-500f, 0f));
            maps.SetTile(bare.Owner, bare.Comp, Vector2i.Zero, map.Tile.Tile);
            // Generators that make one plain anomaly, placed exactly: the usual spawner offsets it a little and may roll a
            // trap. Each is set generating with its run already over, so the next update finishes it.
            anomaliesBefore = Anomalies(em).ToHashSet();
            var shipGenerator = em.SpawnEntity("WGTestAnomalyGenerator", em.GetComponent<TransformComponent>(chamberMarker).Coordinates);
            var bareGenerator = em.SpawnEntity("WGTestAnomalyGenerator", new EntityCoordinates(bareGrid, new Vector2(0.5f, 0.5f)));
            em.EnsureComponent<GeneratingAnomalyGeneratorComponent>(shipGenerator);
            em.EnsureComponent<GeneratingAnomalyGeneratorComponent>(bareGenerator);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var made = Anomalies(em).Where(a => !anomaliesBefore.Contains(a)).ToList();
            var markerXform = em.GetComponent<TransformComponent>(chamberMarker);
            Assert.That(made.Where(a => em.GetComponent<TransformComponent>(a).GridUid == grid).ToList(), Has.Count.EqualTo(1)
                .And.All.Matches<EntityUid>(a => (em.GetComponent<TransformComponent>(a).LocalPosition - markerXform.LocalPosition).Length() < 0.01f),
                "The Lupercal's generator puts its anomaly on the chamber's marker.");
            // The bare grid has no air, so the random search finds no tile and the usual fallback, the grid's own origin, is
            // where it lands; either way it is the generator's usual placing, not the Lupercal's chamber.
            Assert.That(made.Count(a => em.GetComponent<TransformComponent>(a).GridUid != grid), Is.EqualTo(1),
                "A generator with no chamber on its grid places its anomaly as it always has, away from the Lupercal.");
            foreach (var anomaly in made)
                em.DeleteEntity(anomaly);
        });

        // A paramedic carries a patient rather than dragging them.
        await server.WaitAssertion(() =>
        {
            var roster = em.GetComponent<WGShipRosterComponent>(grid);
            var medic = roster.Slots.Values.First(s => s.Role.Id == "WGCrewParamedic" && s.Occupant != null).Occupant!.Value;
            var patient = em.SpawnEntity("MobHuman", em.GetComponent<TransformComponent>(medic).Coordinates);
            var carrying = server.System<CarryingSystem>();
            Assert.That(carrying.TryCrewCarry(medic, patient), Is.True, "The paramedic picks the patient up.");
            Assert.That(em.GetComponent<BeingCarriedComponent>(patient).Carrier, Is.EqualTo(medic));
            carrying.DropCarried(medic, patient);
            em.DeleteEntity(patient);
        });

    }

    private static List<EntityUid> Anomalies(IEntityManager em)
    {
        var found = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<AnomalyComponent>();
        while (query.MoveNext(out var uid, out _))
            found.Add(uid);

        return found;
    }
}
