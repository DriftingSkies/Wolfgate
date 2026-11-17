#nullable enable
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.Encounters;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

/// <summary>
/// A ship minding only known threats leaves a player hull running dark alone, where a plain patrol fires on it as an
/// unknown; an enemy NPC hull running dark is still fought on sight.
/// </summary>
public sealed partial class WGEncounterTest
{
    private static ProtoId<WGEncounterPrototype> WGTestEncounterKnownThreatGuardProto => "WGTestEncounterKnownThreatGuard";
    private static ProtoId<WGEncounterPrototype> WGTestEncounterPlainGuardProto => "WGTestEncounterPlainGuard";
    private static ProtoId<WGEncounterPrototype> WGTestEncounterKnownThreatRaiderProto => "WGTestEncounterKnownThreatRaider";

    [TestPrototypes]
    private const string KnownThreatPrototypes = @"
- type: wgEncounter
  id: WGTestEncounterKnownThreatGuard
  name: wg-encounter-name-judge-patrol
  start: Manual
  ships:
  - key: guard
    vessel: WGDredger
    captain: false
    company: WGJudges
    zoneTargets: AtWar
    knownThreatsOnly: true
    warnRange: 1000
    attackRange: 600

- type: wgEncounter
  id: WGTestEncounterPlainGuard
  name: wg-encounter-name-patrol
  start: Manual
  ships:
  - key: guard
    vessel: WGDredger
    captain: false
    company: WGJudges
    zoneTargets: AtWar
    warnRange: 1000
    attackRange: 600

- type: wgEncounter
  id: WGTestEncounterKnownThreatRaider
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: raider
    vessel: WGDredger
    captain: false
    company: WGAshfallReavers
    zoneTargets: AtWar
    warnRange: 1000
    attackRange: 400
";

    [Test]
    public async Task KnownThreatsOnlyLeavesAPlayerHullRunningDarkAlone()
    {
        var encounters = Server.System<WGEncounterSystem>();
        var prototypes = Server.ResolveDependency<IPrototypeManager>();
        var alerts = Server.System<WGCrewAlertSystem>();
        var at = new Vector2(86000, 86000);
        WGEncounterShipState judge = default!, plain = default!;
        EntityUid player = default, dark = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(encounters.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestEncounterKnownThreatGuardProto),
                new MapCoordinates(at, MapData.MapId), out var judging), Is.True);
            Assert.That(encounters.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestEncounterPlainGuardProto),
                new MapCoordinates(at + new Vector2(0, 400), MapData.MapId), out var guarding), Is.True);
            judge = SEntMan.GetComponent<WGEncounterComponent>(judging).Ships["guard"];
            plain = SEntMan.GetComponent<WGEncounterComponent>(guarding).Ships["guard"];
            player = ClampDeck(at + new Vector2(250, 200)).Grid;
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(player, new Vector2(1.5f, 0.5f)));
            Server.System<ShuttleSystem>().AddIFFFlag(player, IFFFlags.HideLabel);
        });

        // Under way, so the plain patrol takes it for an unknown and not just a hulk lying still.
        for (var second = 0; second < 4; second++)
        {
            await Server.WaitPost(() => Server.System<SharedPhysicsSystem>().SetLinearVelocity(player, new Vector2(5f, 0f)));
            await RunTicks(Ticks(1));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(plain.Engaged.Contains(player) || alerts.IsWarningShot(plain.Grid, player), Is.True,
                $"A plain patrol fires on a player running dark (engaged {plain.Engaged.Count}, strangers {plain.Strangers.Count}).");
            Assert.That(judge.Engaged.Contains(player), Is.False, "The judges leave a player running dark alone.");
            Assert.That(alerts.IsWarningShot(judge.Grid, player), Is.False, "And fire no warning shots at him.");
            Assert.That(alerts.IsHostileShip(judge.Grid, judge.Group, player), Is.False);

            Assert.That(encounters.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestEncounterKnownThreatRaiderProto),
                new MapCoordinates(at + new Vector2(-300, 0), MapData.MapId), out var raid), Is.True);
            var raider = SEntMan.GetComponent<WGEncounterComponent>(raid).Ships["raider"];
            Server.System<ShuttleSystem>().AddIFFFlag(raider.Grid, IFFFlags.HideLabel);
            dark = raider.Grid;
        });
        await RunTicks(Ticks(3));
        await Server.WaitAssertion(() =>
        {
            Assert.That(alerts.IsHostileShip(judge.Grid, judge.Group, dark), Is.True,
                "An enemy NPC hull running dark is still fought on sight.");
        });
    }

    /// <summary>
    /// A navy patrol knows station staff running dark, as a judge in a Roswell does, and leaves them alone, where it
    /// fires on anyone else running dark.
    /// </summary>
    [Test]
    public async Task NavyPatrolsKnowStationStaffRunningDark()
    {
        var encounters = Server.System<WGEncounterSystem>();
        var prototypes = Server.ResolveDependency<IPrototypeManager>();
        var alerts = Server.System<WGCrewAlertSystem>();
        var at = new Vector2(-86000, 86000);
        WGEncounterShipState patrol = default!;
        EntityUid player = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(encounters.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestEncounterPlainGuardProto),
                new MapCoordinates(at, MapData.MapId), out var guarding), Is.True);
            patrol = SEntMan.GetComponent<WGEncounterComponent>(guarding).Ships["guard"];
            player = ClampDeck(at + new Vector2(250, 200)).Grid;
            Server.System<SharedTransformSystem>().SetCoordinates(SPlayer, new EntityCoordinates(player, new Vector2(1.5f, 0.5f)));
            Server.System<ShuttleSystem>().AddIFFFlag(player, IFFFlags.HideLabel);
            SEntMan.EnsureComponent<CompanyComponent>(SPlayer).CompanyName = "Colonial";
        });

        for (var second = 0; second < 4; second++)
        {
            await Server.WaitPost(() => Server.System<SharedPhysicsSystem>().SetLinearVelocity(player, new Vector2(5f, 0f)));
            await RunTicks(Ticks(1));
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(patrol.Engaged.Contains(player), Is.False, "Station staff running dark are not fired on.");
            Assert.That(alerts.IsWarningShot(patrol.Grid, player), Is.False, "Nor shot across the bows.");
            Assert.That(alerts.IsHostileShip(patrol.Grid, patrol.Group, player), Is.False);
            SEntMan.RemoveComponent<CompanyComponent>(SPlayer);
        });
    }

    private int Ticks(double seconds) => (int) Math.Ceiling(seconds * STiming.TickRate);
}
