#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.Power.Components;
using Content.Server.Power.Generator;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.Encounters;
using Content.Shared.Power.Components;
using Content.Shared.Power.Generator;
using Content.Shared.Radio.Components;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

/// <summary>Judging an encounter that begins beside another, stranded ships, and who speaks and in what colour.</summary>
public sealed partial class WGEncounterTest
{
    private static ProtoId<WGEncounterPrototype> WGTestCoreRunningProto => "WGTestCoreRunning";
    private static ProtoId<WGEncounterPrototype> WGTestCoreWaitingProto => "WGTestCoreWaiting";
    private static ProtoId<WGEncounterPrototype> WGTestCoreStrandedProto => "WGTestCoreStranded";
    private static ProtoId<WGEncounterPrototype> WGTestCoreTwoSidesProto => "WGTestCoreTwoSides";

    [TestPrototypes]
    private const string BatchCorePrototypes = @"
- type: wgEncounter
  id: WGTestCoreRunning
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: lone
    vessel: WGDredger
    objectives:
    - kind: Hold
      duration: 600

- type: wgEncounter
  id: WGTestCoreWaiting
  name: wg-encounter-name-convoy
  start: Manual
  startRadius: 300
  ships:
  - key: lone
    vessel: WGDredger
    objectives:
    - kind: Hold
      duration: 600

- type: wgEncounter
  id: WGTestCoreStranded
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: dry
    vessel: WGDredger
    side: stranded
    stranded: Fuel
    objectives:
    - kind: GoTo
      offset: 2000, 0
  - key: wrecked
    vessel: WGDredger
    offset: 300, 0
    side: stranded
    stranded: Thrusters
    objectives:
    - kind: GoTo
      offset: 2000, 0

- type: wgEncounter
  id: WGTestCoreTwoSides
  name: wg-encounter-name-convoy
  start: Manual
  announcement: wg-encounter-announce-backup
  announceOnRadio: true
  announcer: caller
  ships:
  - key: raider
    vessel: WGDredger
    side: raiders
    captain: false
    deckhands: 0
    roles: [ WGCrewPilot ]
  - key: caller
    vessel: WGDredger
    offset: 300, 0
    side: callers
    captain: false
    deckhands: 0
    roles: [ WGCrewPilot ]
    iffColor: ""#123456""
";

    /// <summary>
    /// An encounter that begins while another is running is judged on the orders it was just given, not on crews read
    /// before it had any: its finite hold is not taken for flown.
    /// </summary>
    [Test]
    public async Task EncounterBegunBesideAnotherIsNotCompletedAtOnce()
    {
        EntityUid running = default, waiting = default;
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WGEncounterSystem>();
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            Assert.That(system.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestCoreRunningProto),
                new MapCoordinates(new Vector2(31000, 31000), MapData.MapId), out running), Is.True);
            Assert.That(system.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestCoreWaitingProto),
                new MapCoordinates(new Vector2(33000, 33000), MapData.MapId), out waiting), Is.True);
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player),
                new EntityCoordinates(MapData.MapUid, new Vector2(33100, 33000)));
        });
        await WaitUntilServer(() => SEntMan.GetComponent<WGEncounterComponent>(waiting).Begun, 900);
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WGEncounterComponent>(waiting);
            Assert.That(comp.Ships["lone"].HasOrders, Is.True);
            Assert.That(comp.Ships["lone"].Flown, Is.False);
            Assert.That(comp.Resolution, Is.Null, "Its hold has only begun.");
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(running).Resolution, Is.Null);

            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            var system = Server.System<WGEncounterSystem>();
            system.End(waiting);
            system.End(running);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A ship stranded for fuel arrives with its generators empty and batteries flat and is not topped up; one with
    /// wrecked thrusters has none left. Both hold their orders back until they are rescued.
    /// </summary>
    [Test]
    public async Task StrandedShipsArriveWithoutFuelOrThrustersAndWait()
    {
        EntityUid encounter = default, dry = default, wrecked = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestCoreStrandedProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(35000, 35000), MapData.MapId), out encounter), Is.True);
            var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
            dry = comp.Ships["dry"].Grid;
            wrecked = comp.Ships["wrecked"].Grid;
            Assert.That(comp.Ships["dry"].Stranding, Is.EqualTo(WGEncounterStranding.Fuel));
            Assert.That(comp.Ships["wrecked"].Stranding, Is.EqualTo(WGEncounterStranding.Thrusters));

            var batteries = SEntMan.EntityQueryEnumerator<PowerNetworkBatteryComponent, BatteryComponent, TransformComponent>();
            while (batteries.MoveNext(out _, out _, out var battery, out var xform))
            {
                if (xform.GridUid == dry)
                    Assert.That(battery.CurrentCharge, Is.Zero, "The batteries are flat.");
            }
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
            var generators = Server.System<GeneratorSystem>();
            var found = 0;
            var fuelled = SEntMan.EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
            while (fuelled.MoveNext(out var uid, out var generator, out var xform))
            {
                if (xform.GridUid != dry)
                    continue;

                found++;
                Assert.That(generators.GetFuel(uid), Is.Zero, "The generator is empty and nobody refuelled it.");
                Assert.That(generator.On, Is.False);
            }

            Assert.That(found, Is.GreaterThan(0));
            var thrusters = SEntMan.EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
            while (thrusters.MoveNext(out _, out var thruster, out var xform))
            {
                Assert.That(xform.GridUid == wrecked && thruster.Type == ThrusterType.Linear, Is.False, "No thruster drives the wrecked ship.");
            }

            foreach (var ship in comp.Ships.Values)
            {
                Assert.That(WGEncounterSystem.IsStranded(ship), Is.True);
                Assert.That(ship.HasOrders, Is.False, "Its orders wait for the rescue.");
            }

            Assert.That(comp.Resolution, Is.Null, "A stranded ship is not out of the encounter for being unable to move.");
            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// Only the announcing ship voices the announcement, though another is listed first; each side shows its radar
    /// colour, its own where one is set.
    /// </summary>
    [Test]
    public async Task AnnouncerSpeaksAndSidesShowTheirColors()
    {
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestCoreTwoSidesProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(37000, 37000), MapData.MapId), out var encounter), Is.True);
            var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
            var raider = comp.Ships["raider"];
            var caller = comp.Ships["caller"];

            var callerSpoke = false;
            var members = SEntMan.EntityQueryEnumerator<WGCrewComponent>();
            while (members.MoveNext(out var uid, out var member))
            {
                if (member.Group == raider.Group)
                    Assert.That(SEntMan.HasComponent<TelecomExemptComponent>(uid), Is.False, "The raider does not speak for the encounter.");
                else if (member.Group == caller.Group && SEntMan.HasComponent<TelecomExemptComponent>(uid))
                    callerSpoke = true;
            }

            Assert.That(callerSpoke, Is.True, "The caller made the announcement.");
            Assert.That(SEntMan.GetComponent<IFFComponent>(raider.Grid).Color, Is.EqualTo(WGEncounterSystem.SideColors[0]));
            Assert.That(SEntMan.GetComponent<IFFComponent>(caller.Grid).Color, Is.EqualTo(Color.FromHex("#123456")));
            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }
}
