#nullable enable
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

/// <summary>A ship minding only NPCs leaves a player ship of an enemy company alone, where a plain patrol fights it.</summary>
public sealed partial class WGEncounterTest
{
    private static ProtoId<WGEncounterPrototype> WGTestEncounterNpcTargetsOnlyGuardProto => "WGTestEncounterNpcTargetsOnlyGuard";

    [TestPrototypes]
    private const string NpcTargetsOnlyPrototypes = @"
- type: wgEncounter
  id: WGTestEncounterNpcTargetsOnlyGuard
  name: wg-encounter-name-judge-patrol
  start: Manual
  ships:
  - key: guard
    vessel: WGDredger
    captain: false
    company: WGJudges
    zoneTargets: AtWar
    npcTargetsOnly: true
    warnRange: 1000
    attackRange: 600
";

    [Test]
    public async Task NpcTargetsOnlyLeavesEnemyPlayerShipsAlone()
    {
        var encounters = Server.System<WGEncounterSystem>();
        var prototypes = Server.ResolveDependency<IPrototypeManager>();
        var alerts = Server.System<WGCrewAlertSystem>();
        var at = new Vector2(92000, 92000);
        WGEncounterShipState judge = default!, plain = default!;
        EntityUid player = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(encounters.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestEncounterNpcTargetsOnlyGuardProto),
                new MapCoordinates(at, MapData.MapId), out var judging), Is.True);
            Assert.That(encounters.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestEncounterPlainGuardProto),
                new MapCoordinates(at + new Vector2(0, 400), MapData.MapId), out var guarding), Is.True);
            judge = SEntMan.GetComponent<WGEncounterComponent>(judging).Ships["guard"];
            plain = SEntMan.GetComponent<WGEncounterComponent>(guarding).Ships["guard"];
            player = ClampDeck(at + new Vector2(250, 200)).Grid;
            SEntMan.EnsureComponent<CompanyComponent>(player).CompanyName = "WGAshfallReavers";
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
