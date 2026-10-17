#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;

namespace Content.IntegrationTests.Tests._WF.Encounters;

/// <summary>Zones answer only to ships, a zone threat is no attack, and a decided encounter pays the side with living crew.</summary>
public sealed partial class WFEncounterTest
{
    [TestPrototypes]
    private const string AuditZonePrototypes = @"
- type: wfEncounter
  id: WFTestAuditZoneGuarded
  name: wf-encounter-name-convoy
  start: Manual
  ships:
  - key: guarded
    vessel: WFDredger
    warnRange: 500
    attackRange: 150

- type: wfEncounter
  id: WFTestAuditZoneSkirmish
  name: wf-encounter-name-convoy
  start: Manual
  rewards:
  - side: good
    spesos: 9000
  ships:
  - key: friend
    vessel: WFDredger
    side: good
  - key: foe
    vessel: WFDredger
    offset: 400, 0
    side: bad
";

    /// <summary>A station with a person on it is not an intruder; the same grid without station membership is.</summary>
    [Test]
    public async Task ZonesIgnoreStationGrids()
    {
        EntityUid encounter = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditZoneGuarded");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(21000, 21000), MapData.MapId), out encounter), Is.True);

            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(21100, 21000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
            SEntMan.EnsureComponent<StationMemberComponent>(intruder);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Warned, Is.Empty, "A station is not warned.");
            Assert.That(state.Engaged, Is.Empty, "A station is not fired on.");
            Assert.That(Server.System<WFCrewAlertSystem>().GetHostileShips(state.Grid, state.Group), Is.Empty);
            SEntMan.RemoveComponent<StationMemberComponent>(intruder);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            Assert.That(state.Engaged.Contains(intruder), Is.True, "A ship in the same place is an intruder.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }

    /// <summary>An intruder reported by a zone is only a zone threat, and no mayday follows, until it really fires.</summary>
    [Test]
    public async Task ZoneThreatIsNotAnAttackUntilTheShipFires()
    {
        EntityUid encounter = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditZoneGuarded");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(22000, 22000), MapData.MapId), out encounter), Is.True);

            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(22100, 22000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WFEncounterComponent>(encounter).Ships["guarded"];
            var alerts = Server.System<WFCrewAlertSystem>();
            Assert.That(alerts.GetHostileShips(state.Grid, state.Group), Does.Contain(intruder));
            Assert.That(alerts.IsZoneThreat(state.Grid, state.Group, intruder), Is.True, "Known only from the zone.");
            Assert.That(alerts.InZoneReport, Is.False, "The zone report is over.");

            var operators = SEntMan.EntityQueryEnumerator<WFRadioOperatorComponent, WFCrewComponent>();
            while (operators.MoveNext(out _, out var radio, out var member))
            {
                if (member.Group != state.Group)
                    continue;

                Assert.That(radio.Alerted, Is.False, "A zone warning starts no attack episode.");
                Assert.That(radio.MaydaySent, Is.False, "A zone warning draws no mayday.");
            }

            var hit = new WFCrewHullHitEvent(state.Grid, intruder);
            SEntMan.EventBus.RaiseLocalEvent(state.Grid, ref hit, true);
            Assert.That(alerts.IsZoneThreat(state.Grid, state.Group, intruder), Is.False, "Once it has fired it is an attacker.");
            Assert.That(alerts.GetHostileShips(state.Grid, state.Group), Does.Contain(intruder));

            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WFEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }

    /// <summary>The side with a ship and living crew has won, whatever state the hulls of the side without crew are in.</summary>
    [Test]
    public async Task DecidedSideIsTheOneWithLivingCrew()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WFEncounterPrototype>("WFTestAuditZoneSkirmish");
            Assert.That(Server.System<WFEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(23000, 23000), MapData.MapId), out var encounter), Is.True);
            var comp = SEntMan.GetComponent<WFEncounterComponent>(encounter);
            var rewards = Server.System<WFEncounterRewardSystem>();
            Assert.That(rewards.DecidedSide(comp), Is.Null, "Both sides still have a crew in the fight.");

            var foe = comp.Ships["foe"];
            var crew = new List<EntityUid>();
            var members = SEntMan.EntityQueryEnumerator<WFCrewComponent>();
            while (members.MoveNext(out var uid, out var member))
            {
                if (member.Group == foe.Group)
                    crew.Add(uid);
            }

            Assert.That(crew, Is.Not.Empty);
            foreach (var uid in crew)
            {
                SEntMan.DeleteEntity(uid);
            }

            Assert.That(SEntMan.Deleted(foe.Grid), Is.False, "Its hull is still there, not yet counted out.");
            Assert.That(rewards.DecidedSide(comp), Is.EqualTo("good"));
            Server.System<WFEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }
}
