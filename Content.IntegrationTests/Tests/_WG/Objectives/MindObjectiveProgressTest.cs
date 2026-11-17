using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Objectives.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Objectives;

/// <summary>An objective held by a mind reports progress from that mind and its body.</summary>
[TestFixture]
[TestOf(typeof(SharedObjectivesSystem))]
public sealed class MindObjectiveProgressTest
{
    private const string Survive = "WGTestSurviveObjective";
    private const string Die = "WGTestDieObjective";
    private const string Escape = "WGTestEscapeObjective";
    private const string Steal = "WGTestStealObjective";
    private const string Loot = "WGTestStealTarget";

    [TestPrototypes]
    private const string Prototypes = @"
- type: stealTargetGroup
  id: WGTestStealGroup
  name: steal-target-groups-hypospray
  sprite:
    sprite: Objects/Specific/Medical/hypospray.rsi
    state: hypo

- type: entity
  id: WGTestStealTarget
  components:
  - type: Item
  - type: StealTarget
    stealGroup:
    - WGTestStealGroup

- type: entity
  abstract: true
  parent: BaseObjective
  id: WGTestBaseObjective
  components:
  - type: Objective
    difficulty: 1
    issuer: objective-issuer-syndicate
    icon:
      sprite: Mobs/Ghosts/ghost_human.rsi
      state: icon

- type: entity
  parent: [WGTestBaseObjective, BaseSurviveObjective]
  id: WGTestSurviveObjective

- type: entity
  parent: WGTestBaseObjective
  id: WGTestDieObjective
  components:
  - type: DieCondition

- type: entity
  parent: WGTestBaseObjective
  id: WGTestEscapeObjective
  components:
  - type: EscapeShuttleCondition

- type: entity
  parent: [WGTestBaseObjective, BaseStealObjective]
  id: WGTestStealObjective
  components:
  - type: StealCondition
    stealGroup: WGTestStealGroup
    verifyMapExistence: false
";

    [Test]
    public async Task ProgressFollowsMindAndBody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var objectives = entMan.System<SharedObjectivesSystem>();
        var mobState = entMan.System<MobStateSystem>();
        var hands = entMan.System<SharedHandsSystem>();

        await server.WaitAssertion(() =>
        {
            var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var loot = entMan.SpawnEntity(Loot, map.GridCoords);
            var mind = mindSys.CreateMind(null);
            mindSys.TransferTo(mind, body);

            EntityUid Create(string proto)
            {
                var objective = objectives.TryCreateObjective(mind.Owner, mind.Comp, proto);
                Assert.That(objective, Is.Not.Null, $"{proto} could not be assigned");
                return objective!.Value;
            }

            // The path the character menu and the round end summary take.
            float? Progress(EntityUid objective) => objectives.GetInfo(objective, mind.Owner, mind.Comp)?.Progress;

            var survive = Create(Survive);
            var die = Create(Die);
            var escape = Create(Escape);
            var steal = Create(Steal);

            Assert.Multiple(() =>
            {
                Assert.That(Progress(survive), Is.EqualTo(1f), "alive: survive");
                Assert.That(Progress(die), Is.EqualTo(0f), "alive: die");
                Assert.That(Progress(escape), Is.EqualTo(0f), "no shuttle: escape");
                Assert.That(Progress(steal), Is.EqualTo(0f), "empty hands: steal");
            });

            Assert.That(hands.TryPickupAnyHand(body, loot), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(Progress(steal), Is.EqualTo(1f), "held: steal");
                Assert.That(objectives.GetProgress(steal), Is.EqualTo(0f), "an objective with no mind answers for itself");
            });

            mobState.ChangeMobState(body, MobState.Dead);
            Assert.Multiple(() =>
            {
                Assert.That(Progress(survive), Is.EqualTo(0f), "dead: survive");
                Assert.That(Progress(die), Is.EqualTo(1f), "dead: die");
            });
        });

        await pair.CleanReturnAsync();
    }
}
