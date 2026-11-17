#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

public sealed partial class WGEncounterTest
{
    private static ProtoId<WGEncounterPrototype> WGTestAuditEscortedProto => "WGTestAuditEscorted";
    private static ProtoId<WGEncounterPrototype> WGTestAuditPersistentProto => "WGTestAuditPersistent";
    private static ProtoId<WGEncounterPrototype> WGTestAuditTraderProto => "WGTestAuditTrader";

    [TestPrototypes]
    private const string AuditLifecyclePrototypes = @"
- type: wgEncounter
  id: WGTestAuditEscorted
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: lead
    vessel: WGDredger
    side: convoy
    objectives:
    - kind: Hold
      duration: 2
  - key: wing
    vessel: WGDredger
    offset: 200, 0
    side: convoy
    captain: false
    objectives:
    - kind: Escort
      target: lead

- type: wgEncounter
  id: WGTestAuditPersistent
  name: wg-encounter-name-convoy
  start: Manual
  lifetime: Persistent
  cost: 2
  ships:
  - key: lone
    vessel: WGDredger

- type: wgEncounter
  id: WGTestAuditTrader
  name: wg-encounter-name-trader
  start: Manual
  ships:
  - key: trader
    vessel: Piva
    side: trader
    profile: WGCrewProfileHauler
    captain: false
    deckhands: 0
    guards: 1
    roles: [ WGCrewPilot, WGCrewDeckhand ]
    passengers: [ WGTraderWanderer ]
    boarding: Ignore
    docking: Ignore
";

    /// <summary>An encounter completes once its ships with finite orders are done, though an escort never finishes.</summary>
    [Test]
    public async Task EncounterCompletesWhileItsEscortStillFliesFormation()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestAuditEscortedProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(27000, 27000), MapData.MapId), out encounter), Is.True);
        });
        await WaitUntilServer(() => !SEntMan.TryGetComponent(encounter, out WGEncounterComponent? comp) || comp.Resolution != null, 1800);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent(encounter, out WGEncounterComponent? comp), Is.True);
            Assert.That(comp!.Resolution, Is.EqualTo(WGEncounterResolution.Completed), "The lead's hold is done; the wing's escort doesn't hold it up.");
            Assert.That(comp.JumpAt, Is.Not.Null);
            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>A round-long encounter takes no slot under the cap and nothing from the budget, and sits on the map itself.</summary>
    [Test]
    public async Task PersistentEncounterTakesNoShareOfTheCapOrBudget()
    {
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WGEncounterSystem>();
            var count = system.ActiveCount();
            var cost = system.ActiveCost();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestAuditPersistentProto);
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(25000, 25000), MapData.MapId), out var encounter), Is.True);
            Assert.That(system.IsRunning("WGTestAuditPersistent"), Is.True);
            Assert.That(system.ActiveCount(), Is.EqualTo(count));
            Assert.That(system.ActiveCost(), Is.EqualTo(cost));
            Assert.That(SEntMan.GetComponent<TransformComponent>(encounter).ParentUid, Is.EqualTo(MapData.MapUid),
                "The encounter is never parented to a grid that could take it away.");
            system.End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A trader with no radio officer and no deckhands still calls for help when its passenger is hurt by hand,
    /// through whoever of its crew is aboard.
    /// </summary>
    [Test]
    public async Task TraderWithoutARadioOfficerCallsForHelpWhenItsPassengerIsHurt()
    {
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WGEncounterSystem>();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestAuditTraderProto);
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(23000, 23000), MapData.MapId), out var encounter), Is.True);
            var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
            var ship = comp.Ships["trader"];

            var crew = new List<(EntityUid Uid, WGCrewComponent Member)>();
            var members = SEntMan.EntityQueryEnumerator<WGCrewComponent>();
            while (members.MoveNext(out var uid, out var member))
            {
                if (member.Group == ship.Group)
                    crew.Add((uid, member));
            }

            Assert.That(crew, Is.Not.Empty);
            Assert.That(crew.Any(entry => entry.Member.Role == WGCrewRoles.Deckhand), Is.False, "No deckhands asked for, none aboard.");
            Assert.That(crew.Any(entry => SEntMan.HasComponent<WGRadioOperatorComponent>(entry.Uid)), Is.False);

            EntityUid? passenger = null;
            var passengers = SEntMan.EntityQueryEnumerator<WGEncounterPassengerComponent>();
            while (passengers.MoveNext(out var uid, out var aboard))
            {
                if (aboard.Encounter == encounter)
                    passenger = uid;
            }

            Assert.That(passenger, Is.Not.Null);
            Server.System<DamageableSystem>().TryChangeDamage(passenger, new DamageSpecifier { DamageDict = { ["Blunt"] = 5 } },
                origin: SEntMan.GetEntity(Player));
            Assert.That(comp.Category, Is.EqualTo(WGEncounterCategory.Distress), "The mayday went out.");
            Assert.That(ship.NextAttackCall, Is.GreaterThan(STiming.CurTime));
            system.End(encounter);
        });
        await RunTicks(10);
    }
}
