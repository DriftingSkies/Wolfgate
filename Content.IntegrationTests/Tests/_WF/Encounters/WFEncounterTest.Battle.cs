#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string BattlePrototypes = @"
- type: wfEncounter
  id: WFTestStoryMelee
  name: wf-encounter-name-fleet-battle
  start: Manual
  ships:
  - key: one
    vessel: WFDredger
    side: one
    warnRange: 800
    attackRange: 800
    objectives:
    - kind: Attack
      target: two
  - key: two
    vessel: WFDredger
    side: two
    offset: 400, 0
    objectives:
    - kind: Attack
      target: one
  - key: three
    vessel: WFDredger
    side: two
    offset: 0, 400
    objectives:
    - kind: Attack
      target: one
";

    /// <summary>
    /// A combatant whose target is gone while the other side still fights turns on the nearest enemy left, and once the
    /// battle is decided the victor's zones stand: a stranger in them is still engaged.
    /// </summary>
    [Test]
    public async Task BattleSurvivorsTurnOnTheNextEnemyAndHoldTheField()
    {
        EntityUid encounter = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestStoryMelee");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-39000, -39000), MapData.MapId), out encounter), Is.True);
            var ships = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships;
            // Its first target is lost with all hands.
            SEntMan.DeleteEntity(ships["two"].Grid);
        });
        // Two polls: one for the flown attack to be noticed, one for the new orders.
        await RunTicks(330);
        await Server.WaitAssertion(() =>
        {
            var ships = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships;
            var one = ships["one"];
            var objectives = Server.System<WFCrewObjectiveSystem>();
            var row = objectives.Snapshot().Single(crew => crew.Grid == SEntMan.GetNetEntity(one.Grid) && crew.Group == one.Group);
            Assert.That(row.Objectives, Is.Not.Empty, "A combatant with its target gone is given new orders.");
            Assert.That(row.Objectives[0].Kind, Is.EqualTo(WFCrewObjectiveKind.Attack));
            Assert.That(row.Objectives[0].Target, Is.EqualTo(SEntMan.GetNetEntity(ships["three"].Grid)), "On the nearest enemy left.");
            Assert.That(SEntMan.GetComponent<WFEncounterComponent>(encounter).Resolution, Is.Null, "The battle goes on.");

            SEntMan.DeleteEntity(ships["three"].Grid);
        });
        await RunTicks(330);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            Assert.That(comp.Resolution, Is.EqualTo(WFEncounterResolution.Decided), "One side left fighting decides the battle.");
            Assert.That(comp.JumpAt, Is.Null, "The victor stays on the field.");

            // A stranger's ship comes into the victor's attack zone.
            var grid = Server.ResolveDependency<IMapManager>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            var here = transform.GetWorldPosition(comp.Ships["one"].Grid);
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, here + new Vector2(300f, 0f)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["one"];
            Assert.That(state.Engaged.Contains(intruder), Is.True, "The victor's zone still fires on a stranger after the battle is decided.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }
}
