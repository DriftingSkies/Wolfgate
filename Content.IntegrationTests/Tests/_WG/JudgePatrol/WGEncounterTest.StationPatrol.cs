#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Mono.Drone.Components;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.JudgePatrol;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.JudgePatrol;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

/// <summary>A station patrol keeps its ships on the orbit, evenly spaced, and puts a lost one back opposite the other.</summary>
public sealed partial class WGEncounterTest
{
    private static ProtoId<WGStationPatrolPrototype> WGTestStationPatrolProto => "WGTestStationPatrol";
    private static readonly ProtoId<WGEncounterPrototype> WGEncounterJudgePatrolProto = "WGEncounterJudgePatrol";

    [TestPrototypes]
    private const string StationPatrolPrototypes = @"
- type: wgEncounter
  id: WGTestEncounterStationPatrol
  name: wg-encounter-name-judge-patrol
  start: Manual
  lifetime: Persistent
  ships:
  - key: judge
    vessel: WGDredger
    captain: false
    objectives:
    - kind: Circle
      target: '@origin'
      range: 600

- type: wgStationPatrol
  id: WGTestStationPatrol
  stations: [ WGTestPatrolPost ]
  encounter: WGTestEncounterStationPatrol
  count: 2
  droneRange: 3000
  respawn: 0
";

    [Test]
    public async Task StationPatrolKeepsTwoShipsOppositeOnTheOrbitAndReplacesALostOne()
    {
        var at = new Vector2(71000, 71000);
        EntityUid station = default;
        await Server.WaitAssertion(() =>
        {
            station = ClampDeck(at).Grid;
            SEntMan.EnsureComponent<StationMemberComponent>(station);
            Server.System<MetaDataSystem>().SetEntityName(station, "WGTestPatrolPost");
        });
        await RunTicks(5);

        var patrols = Server.System<WGStationPatrolSystem>();
        var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGStationPatrolPrototype>(WGTestStationPatrolProto);
        EntityUid first = default;
        EntityUid second = default;
        await Server.WaitAssertion(() =>
        {
            var slots = patrols.Run(prototype);
            Assert.That(slots, Has.Count.EqualTo(2));
            Assert.That(slots[0], Is.Not.Null, "The first slot is flown.");
            Assert.That(slots[1], Is.Not.Null, "The second slot is flown.");
            first = slots[0]!.Value;
            second = slots[1]!.Value;
            AssertOpposite(station, first, second);
        });

        await Server.WaitAssertion(() => Server.System<WGEncounterSystem>().End(first));
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var slots = patrols.Run(prototype);
            Assert.That(slots[1], Is.EqualTo(second), "The other ship flies on.");
            Assert.That(slots[0], Is.Not.Null.And.Not.EqualTo(first), "The lost ship is replaced once its respawn is up.");
            AssertOpposite(station, slots[0]!.Value, second);
        });
    }

    [Test]
    public async Task StationPatrolGoesAfterDronesNearItsStationAndReturnsToItsOrbit()
    {
        var at = new Vector2(76000, 76000);
        EntityUid station = default;
        await Server.WaitAssertion(() =>
        {
            station = ClampDeck(at).Grid;
            SEntMan.EnsureComponent<StationMemberComponent>(station);
            Server.System<MetaDataSystem>().SetEntityName(station, "WGTestPatrolPost");
        });
        await RunTicks(5);

        var patrols = Server.System<WGStationPatrolSystem>();
        var objectives = Server.System<WGCrewObjectiveSystem>();
        var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGStationPatrolPrototype>(WGTestStationPatrolProto);
        var ships = new List<WGEncounterShipState>();
        await Server.WaitAssertion(() =>
        {
            foreach (var slot in patrols.Run(prototype))
            {
                ships.Add(SEntMan.GetComponent<WGEncounterComponent>(slot!.Value).Ships["judge"]);
            }
        });
        await RunTicks(5);

        EntityUid far = default;
        EntityUid near = default;
        await Server.WaitAssertion(() =>
        {
            far = ClampDeck(at + new Vector2(0, 4000)).Grid;
            SEntMan.EnsureComponent<HostileDroneGridComponent>(far);
            patrols.Run(prototype);
            foreach (var ship in ships)
            {
                Assert.That(objectives.AttackTarget(ship.Grid, ship.Group), Is.Null, "A drone beyond the drone range is left be.");
            }

            near = ClampDeck(at + new Vector2(0, 2500)).Grid;
            SEntMan.EnsureComponent<HostileDroneGridComponent>(near);
            patrols.Run(prototype);
            foreach (var ship in ships)
            {
                Assert.That(objectives.AttackTarget(ship.Grid, ship.Group), Is.EqualTo(near), "Every patrol ship goes after the drone.");
            }
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            SEntMan.DeleteEntity(near);
            SEntMan.DeleteEntity(far);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            patrols.Run(prototype);
            foreach (var ship in ships)
            {
                Assert.That(objectives.AttackTarget(ship.Grid, ship.Group), Is.Null, "With the drone gone the ship stops attacking.");
                Assert.That(objectives.QueueStatus(ship.Grid, ship.Group), Is.Not.EqualTo("target-lost"), "And takes up its orbit again.");
            }
        });
    }

    /// <summary>The real judge patrol flies with its pilot, gunner and two guards, every one of them elite.</summary>
    [Test]
    public async Task JudgePatrolCrewIsElite()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGEncounterJudgePatrolProto);
            var station = ClampDeck(new Vector2(81000, 83000)).Grid;
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(81000, 81000), MapData.MapId), out var encounter,
                stops: new List<EntityUid> { station }), Is.True);
            var grid = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["judge"].Grid;
            var crew = new List<WGCrewComponent>();
            var query = SEntMan.EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
            while (query.MoveNext(out _, out var member, out var xform))
            {
                if (xform.GridUid == grid)
                    crew.Add(member);
            }

            Assert.That(crew, Has.Count.EqualTo(4), "A pilot, a gunner and two guards.");
            Assert.That(crew.Select(member => member.Skill), Is.All.EqualTo(WGCrewSkill.Elite));
            Server.System<WGEncounterSystem>().End(encounter);
        });
    }

    /// <summary>Both encounters' ships sit on the orbit's radius from the station's centre, half a turn apart.</summary>
    private void AssertOpposite(EntityUid station, EntityUid first, EntityUid second)
    {
        var xforms = Server.System<SharedTransformSystem>();
        var grid = SEntMan.GetComponent<MapGridComponent>(station);
        var centre = Vector2.Transform(grid.LocalAABB.Center, xforms.GetWorldMatrix(station));
        var a = xforms.GetWorldPosition(SEntMan.GetComponent<WGEncounterComponent>(first).Ships["judge"].Grid) - centre;
        var b = xforms.GetWorldPosition(SEntMan.GetComponent<WGEncounterComponent>(second).Ships["judge"].Grid) - centre;
        Assert.That(a.Length(), Is.EqualTo(600f).Within(60f), "The first ship starts on the orbit.");
        Assert.That(b.Length(), Is.EqualTo(600f).Within(60f), "The second ship starts on the orbit.");
        var apart = MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)), -1f, 1f));
        Assert.That(apart, Is.EqualTo(MathF.PI).Within(0.5f), "The two ships start opposite each other.");
    }
}
