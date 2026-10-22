#nullable enable
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WF.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>A ship minding only NPCs leaves a player ship of an enemy company alone, where a plain patrol fights it.</summary>
public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string NpcTargetsOnlyPrototypes = @"
- type: wfEncounter
  id: WFTestEncounterNpcTargetsOnlyGuard
  name: wf-encounter-name-judge-patrol
  start: Manual
  ships:
  - key: guard
    vessel: WFDredger
    captain: false
    company: WFJudges
    zoneTargets: AtWar
    npcTargetsOnly: true
    warnRange: 1000
    attackRange: 600
";

    [Test]
    public async Task NpcTargetsOnlyLeavesEnemyPlayerShipsAlone()
    {
        var encounters = Server.System<WFEncounterSystem>();
        var prototypes = Server.ResolveDependency<IPrototypeManager>();
        var alerts = Server.System<WFCrewAlertSystem>();
        var at = new Vector2(92000, 92000);
        WFEncounterShipState judge = default!, plain = default!;
        EntityUid player = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(encounters.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestEncounterNpcTargetsOnlyGuard"),
                new MapCoordinates(at, MapData.MapId), out var judging), Is.True);
            Assert.That(encounters.TrySpawn(prototypes.Index<WFEncounterPrototype>("WFTestEncounterPlainGuard"),
                new MapCoordinates(at + new Vector2(0, 400), MapData.MapId), out var guarding), Is.True);
            judge = SEntMan.GetComponent<WFEncounterComponent>(judging).Ships["guard"];
            plain = SEntMan.GetComponent<WFEncounterComponent>(guarding).Ships["guard"];
            player = ClampDeck(at + new Vector2(250, 200)).Grid;
            SEntMan.EnsureComponent<CompanyComponent>(player).CompanyName = "WFAshfallReavers";
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(player, new Vector2(1.5f, 0.5f)));
        });

        for (var second = 0; second < 4; second++)
        {
            await Server.WaitPost(() => Server.System<SharedPhysicsSystem>().SetLinearVelocity(player, new Vector2(5f, 0f)));
            await RunTicks(Ticks(1));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(plain.Engaged.Contains(player) || alerts.IsWarningShot(plain.Grid, player), Is.True,
                "A plain patrol fights a Reaver player ship.");
            Assert.That(judge.Engaged.Contains(player), Is.False, "A ship minding only NPCs leaves it alone.");
            Assert.That(alerts.IsWarningShot(judge.Grid, player), Is.False);
            Assert.That(alerts.IsHostileShip(judge.Grid, judge.Group, player), Is.False);
        });
    }
}
