#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.Encounters.Systems;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Encounters;

public sealed partial class WGEncounterTest
{
    private static ProtoId<WGEncounterPrototype> WGTestStoryRoundStartProto => "WGTestStoryRoundStart";
    private static ProtoId<WGEncounterPrototype> WGTestStoryDockProto => "WGTestStoryDock";
    private static ProtoId<WGEncounterPrototype> WGTestStoryIffProto => "WGTestStoryIff";
    private static ProtoId<WGEncounterPrototype> WGTestStoryEveryoneProto => "WGTestStoryEveryone";
    private static ProtoId<WGEncounterPrototype> WGTestStoryFighterProto => "WGTestStoryFighter";
    private static ProtoId<WGEncounterPrototype> WGTestStoryUnionProto => "WGTestStoryUnion";
    private static readonly ProtoId<Content.Shared.Dataset.DatasetPrototype> WGNamesUsspLastProto = "WGNamesUsspLast";
    private static ProtoId<WGEncounterPrototype> WGTestStoryHulkProto => "WGTestStoryHulk";
    private static ProtoId<WGEncounterPrototype> WGTestStoryLeashProto => "WGTestStoryLeash";

    [TestPrototypes]
    private const string StorytellerPrototypes = @"
- type: wgEncounter
  id: WGTestStoryRoundStart
  name: wg-encounter-name-convoy
  start: RoundStart
  weight: 0
  cost: 3
  ships:
  - key: lone
    vessel: WGDredger

- type: wgEncounter
  id: WGTestStoryFighter
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: fighter
    vessel: WGDredger
    company: TSF
    deckhands: 0
    captain: false
    roles: [ WGCrewPilot, WGCrewGunner ]

- type: wgEncounter
  id: WGTestStoryUnion
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: ship
    vessel: WGDredger
    company: USSP
    faction: USSP
    profile: WGCrewProfileUssp

- type: wgEncounter
  id: WGTestStoryIff
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: patrol
    vessel: WGDredger
    company: TSF
    warnRange: 800
    attackRange: 300
    zoneTargets: AtWar

- type: wgEncounter
  id: WGTestStoryEveryone
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: warship
    vessel: WGDredger
    company: TSF
    warnRange: 800
    attackRange: 300
    zoneTargets: Everyone

- type: wgEncounter
  id: WGTestStoryRival
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: rival
    vessel: WGDredger
    company: PDV

- type: wgEncounter
  id: WGTestStoryHulk
  name: wg-encounter-name-hulk
  start: Manual
  ships:
  - key: hulk
    vessel: WGDredger
    distress: false
    derelict:
      dwellers: 3
      dwellerPool: [ WGMobDweller, WGMobDwellerHooded ]
      king: WGMobDwellerKing
      debris: 5
      debrisPool: [ ShardGlass ]

- type: wgEncounter
  id: WGTestStoryLeash
  name: wg-encounter-name-convoy
  start: Manual
  leash: 1000
  ships:
  - key: one
    vessel: WGDredger
    side: one
    objectives:
    - kind: Attack
      target: two
  - key: two
    vessel: WGDredger
    side: two
    offset: 400, 0
    objectives:
    - kind: Attack
      target: one

- type: wgEncounter
  id: WGTestStoryDock
  name: wg-encounter-name-convoy
  start: Manual
  ships:
  - key: lead
    vessel: WGDredger
    side: convoy
    objectives:
    - kind: Dock
      target: berth
    - kind: Hold
      duration: 300
    - kind: Undock
    - kind: GoTo
      offset: 3000, 0
      range: 200
  - key: berth
    vessel: WGDredger
    offset: 300, 0
    side: convoy
";

    /// <summary>An encounter placed at round start takes no slot under the cap and nothing from the budget, round-long or not.</summary>
    [Test]
    public async Task RoundStartEncounterTakesNothingFromTheBudget()
    {
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<WGEncounterSystem>();
            var count = system.ActiveCount();
            var cost = system.ActiveCost();
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryRoundStartProto);
            Assert.That(system.TrySpawn(prototype, new MapCoordinates(new Vector2(33000, 33000), MapData.MapId), out var encounter), Is.True);
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(encounter).OffBudget, Is.True);
            Assert.That(system.ActiveCount(), Is.EqualTo(count));
            Assert.That(system.ActiveCost(), Is.EqualTo(cost));
            system.End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>A ship that cannot dock at a stop waits off it a short while in place of the call, then flies on.</summary>
    [Test]
    public async Task ShipThatCannotDockLoitersThenMovesOn()
    {
        EntityUid encounter = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryDockProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(31000, 31000), MapData.MapId), out encounter), Is.True);
            var comp = SEntMan.GetComponent<WGEncounterComponent>(encounter);
            var lead = comp.Ships["lead"];
            var pilot = EntityUid.Invalid;
            var pilots = SEntMan.EntityQueryEnumerator<WGPilotDutyComponent, WGCrewComponent>();
            while (pilots.MoveNext(out var uid, out _, out var crew))
            {
                if (crew.Group == lead.Group)
                    pilot = uid;
            }

            Assert.That(pilot.IsValid(), Is.True, "The ship has a pilot.");
            var failed = new WGPilotDockFailedEvent(pilot, lead.Grid, comp.Ships["berth"].Grid);
            SEntMan.EventBus.RaiseEvent(EventSource.Local, ref failed);
            Assert.That(Server.System<WGCrewObjectiveSystem>().QueueStatus(lead.Grid, lead.Group), Is.EqualTo("dock-failed"));
        });
        await RunTicks(400);
        await Server.WaitAssertion(() =>
        {
            var objectives = Server.System<WGCrewObjectiveSystem>();
            var lead = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["lead"];
            var net = SEntMan.GetNetEntity(lead.Grid);
            var row = objectives.Snapshot().Single(crew => crew.Grid == net && crew.Group == lead.Group);
            Assert.That(objectives.QueueStatus(lead.Grid, lead.Group), Is.Not.EqualTo("dock-failed"));
            Assert.That(row.Objectives, Is.Not.Empty);
            Assert.That(row.Objectives[0].Kind, Is.EqualTo(WGCrewObjectiveKind.Hold), "It waits off the stop.");
            Assert.That(row.Objectives[0].Duration, Is.GreaterThan(0f).And.LessThanOrEqualTo(90f), "And not for the whole call.");
            Assert.That(row.Objectives.Any(item => item.Kind == WGCrewObjectiveKind.Dock), Is.False);
            Assert.That(row.Objectives.Any(item => item.Kind == WGCrewObjectiveKind.GoTo), Is.True, "The rest of its orders stand.");
            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A patrol cannot tell whose a ship is with its IFF off: a neutral ship is left alone while it shows, only hailed
    /// while it hides lying still, challenged with warning shots in the warning zone once it is under way, and fired
    /// on in the attack zone.
    /// </summary>
    [Test]
    public async Task PatrolChallengesAShipWithItsIffOff()
    {
        EntityUid encounter = default, patrol = default, intruder = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryIffProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(35000, 35000), MapData.MapId), out encounter), Is.True);
            patrol = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["patrol"].Grid;

            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(35600, 35000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(intruder).CompanyName = "USSP";
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var alerts = Server.System<WGCrewAlertSystem>();
            Assert.That(alerts.IsWarningShot(patrol, intruder), Is.False, "A neutral ship showing its IFF is left alone.");
            Server.System<Content.Server.Shuttles.Systems.ShuttleSystem>().AddIFFFlag(intruder, Content.Shared.Shuttles.Components.IFFFlags.HideLabel);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewAlertSystem>().IsWarningShot(patrol, intruder), Is.False, "Masked but lying still, it is only hailed.");
            // Under way from here on.
            var body = SEntMan.EnsureComponent<Robust.Shared.Physics.Components.PhysicsComponent>(intruder);
            var physics = Server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            physics.SetBodyType(intruder, Robust.Shared.Physics.BodyType.Dynamic, body: body);
            physics.SetLinearDamping(intruder, body, 0f);
            physics.SetLinearVelocity(intruder, new Vector2(0f, 4f), body: body);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["patrol"];
            Assert.That(Server.System<WGCrewAlertSystem>().IsWarningShot(patrol, intruder), Is.True, "Masked, it gets warning shots.");
            Assert.That(state.Engaged, Is.Empty, "But is not fired on in the warning zone.");
            Server.System<SharedTransformSystem>().SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(35150, 35000)));
            Server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(intruder, new Vector2(0f, 4f));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["patrol"];
            Assert.That(state.Engaged.Contains(intruder), Is.True, "In the attack zone it is engaged.");
            Assert.That(Server.System<WGCrewAlertSystem>().IsWarningShot(patrol, intruder), Is.False, "And the shots are no longer warnings.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WGEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A round from a ship's guns that comes inside a patrol's attack zone is an attack, hit or miss, and the patrol
    /// takes the ship that fired it for an enemy wherever it is, beyond its zones included.
    /// </summary>
    [Test]
    public async Task FireIntoTheAttackZoneIsAnAttack()
    {
        EntityUid encounter = default, patrol = default, intruder = default;
        var group = string.Empty;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryIffProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(33000, 33000), MapData.MapId), out encounter), Is.True);
            var state = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["patrol"];
            patrol = state.Grid;
            group = state.Group;

            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            intruder = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            // Well outside both zones.
            transform.SetCoordinates(intruder, new EntityCoordinates(MapData.MapUid, new Vector2(34000, 33000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(intruder, new Vector2(0.5f)));
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(intruder).CompanyName = "USSP";
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewAlertSystem>().IsHostileShip(patrol, group, intruder), Is.False, "A neutral ship beyond the zones is nobody's enemy.");
            // A round from one of its guns lands short, inside the attack zone.
            var gun = SEntMan.SpawnEntity("SheetSteel1", new EntityCoordinates(intruder, new Vector2(0.5f)));
            var centre = Server.System<SharedTransformSystem>().GetWorldPosition(patrol);
            var round = SEntMan.SpawnEntity("BulletKinetic", new MapCoordinates(centre + new Vector2(150f, 0f), MapData.MapId));
            var projectile = SEntMan.GetComponent<Content.Shared.Projectiles.ProjectileComponent>(round);
            projectile.Weapon = gun;
            projectile.Shooter = gun;
            // Left hanging in space so the poll finds it.
            SEntMan.RemoveComponent<Robust.Shared.Spawners.TimedDespawnComponent>(round);
        });
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<WGCrewAlertSystem>().IsHostileShip(patrol, group, intruder), Is.True, "Fire into the attack zone makes the ship that fired it an enemy.");
            Assert.That(Server.System<WGCrewAlertSystem>().IsWarningShot(patrol, intruder), Is.False, "And it gets no warning shots.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WGEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(intruder);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A warship that minds everyone but its own company still lets its own faction's other companies be: a
    /// Federation civilian in a Federation attack zone is no intruder, where a Union ship is.
    /// </summary>
    [Test]
    public async Task OwnFactionIsNoIntruderToAnEveryoneZone()
    {
        EntityUid encounter = default, warship = default, visitor = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryEveryoneProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(31000, 31000), MapData.MapId), out encounter), Is.True);
            warship = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["warship"].Grid;

            var grid = Server.System<SharedMapSystem>().CreateGridEntity(MapData.MapId);
            visitor = grid.Owner;
            Server.System<SharedMapSystem>().SetTile(grid, Vector2i.Zero, new Tile(1));
            var transform = Server.System<SharedTransformSystem>();
            // Inside the attack zone, under way.
            transform.SetCoordinates(visitor, new EntityCoordinates(MapData.MapUid, new Vector2(31150, 31000)));
            transform.SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(visitor, new Vector2(0.5f)));
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(visitor).CompanyName = "TSFCivilian";
            SEntMan.EnsureComponent<Content.Shared._Mono.Company.CompanyComponent>(SEntMan.GetEntity(Player)).CompanyName = "TSFCivilian";
            // With its IFF off: its own faction knows its crew over the radio all the same.
            Server.System<Content.Server.Shuttles.Systems.ShuttleSystem>().AddIFFFlag(visitor, Content.Shared.Shuttles.Components.IFFFlags.HideLabel);
            var body = SEntMan.EnsureComponent<Robust.Shared.Physics.Components.PhysicsComponent>(visitor);
            var physics = Server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            physics.SetBodyType(visitor, Robust.Shared.Physics.BodyType.Dynamic, body: body);
            physics.SetLinearDamping(visitor, body, 0f);
            physics.SetLinearVelocity(visitor, new Vector2(0f, 4f), body: body);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["warship"];
            Assert.That(state.Engaged, Is.Empty, "A Federation civilian is no intruder to a Federation warship, IFF or no.");
            Assert.That(state.Warned, Is.Empty, "Nor is it warned off.");
            SEntMan.GetComponent<Content.Shared._Mono.Company.CompanyComponent>(visitor).CompanyName = "USSP";
            SEntMan.GetComponent<Content.Shared._Mono.Company.CompanyComponent>(SEntMan.GetEntity(Player)).CompanyName = "USSP";
            Server.System<SharedTransformSystem>().SetCoordinates(visitor, new EntityCoordinates(MapData.MapUid, new Vector2(31150, 31000)));
            Server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>().SetLinearVelocity(visitor, new Vector2(0f, 4f));
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var state = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["warship"];
            Assert.That(state.Engaged.Contains(visitor), Is.True, "A Union ship in the same place is fired on.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
            Server.System<WGEncounterSystem>().End(encounter);
            SEntMan.DeleteEntity(visitor);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A finished ship jumps out only once nobody is aboard it, so a player working on a hull never has it vanish from
    /// under them. And an encounter ship loads no world around itself: its consoles come without world loaders.
    /// </summary>
    [Test]
    public async Task FinishedShipWaitsForPlayersToLeaveAndLoadsNoWorld()
    {
        EntityUid encounter = default, ship = default;
        var config = Server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>();
        await Server.WaitAssertion(() =>
        {
            // No grace after the player leaves; ResolvedShipWaitsOutTheGraceAfterPlayersLeave covers it.
            config.SetCVar(EncountersCVars.CleanupGrace, 0f);
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryRivalProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-31000, -31000), MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["rival"].Grid;

            var loaders = SEntMan.EntityQueryEnumerator<Content.Server.Worldgen.Components.WorldLoaderComponent, TransformComponent>();
            while (loaders.MoveNext(out _, out _, out var xform))
                Assert.That(xform.GridUid, Is.Not.EqualTo(ship), "An encounter ship's consoles load no world.");

            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(ship, new Vector2(0.5f)));
            Server.System<WGEncounterSystem>().Resolve(encounter, WGEncounterResolution.Completed);
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(encounter).JumpAt, Is.Not.Null, "A completed transient encounter jumps out.");
        });
        // Well past the jump delay of 20 s.
        await RunTicks(900);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(ship), Is.True, "A ship with a player aboard waits to jump.");
            Server.System<SharedTransformSystem>().SetCoordinates(SEntMan.GetEntity(Player), new EntityCoordinates(MapData.MapUid, Vector2.Zero));
        });
        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(ship), Is.False, "Once the player has left, it jumps.");
            config.SetCVar(EncountersCVars.CleanupGrace, EncountersCVars.CleanupGrace.DefaultValue);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A crewed ship arrives with its plant commissioned and fuel stowed, and carries an engineer who refuels a
    /// generator that runs dry from the stores by hand.
    /// </summary>
    [Test]
    public async Task EngineerRefuelsTheGeneratorFromStores()
    {
        EntityUid encounter = default, ship = default, generator = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryRivalProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-33000, -33000), MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["rival"].Grid;

            var engineers = 0;
            var crew = SEntMan.EntityQueryEnumerator<WGCrewEngineerComponent, WGCrewComponent, TransformComponent>();
            while (crew.MoveNext(out _, out _, out var member, out var xform))
            {
                if (xform.GridUid == ship)
                {
                    engineers++;
                    Assert.That(member.Role, Is.EqualTo(WGCrewRoles.Engineer));
                }
            }
            Assert.That(engineers, Is.EqualTo(1), "One engineer is posted by the plant.");

            var generators = SEntMan.EntityQueryEnumerator<Content.Shared.Power.Generator.FuelGeneratorComponent, TransformComponent>();
            while (generators.MoveNext(out var uid, out var fuel, out var xform))
            {
                if (xform.GridUid != ship)
                    continue;
                generator = uid;
                Assert.That(Server.System<Content.Server.Power.Generator.GeneratorSystem>().GetFuel(uid), Is.GreaterThan(0f), "The generator is fuelled at spawn.");
                Assert.That(fuel.On, Is.True, "And lit.");
            }
            Assert.That(generator, Is.Not.EqualTo(default(EntityUid)), "The test hull has a generator.");

            var stores = 0;
            var crates = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (crates.MoveNext(out _, out var meta, out var xform))
            {
                if (xform.GridUid == ship && meta.EntityPrototype?.ID == "WGCrewFuelStores")
                    stores++;
            }
            Assert.That(stores, Is.EqualTo(1), "Fuel for the trip is stowed in one crate.");

            Server.System<Content.Server.Power.Generator.GeneratorSystem>().EmptyGenerator(generator);
            Assert.That(Server.System<Content.Server.Power.Generator.GeneratorSystem>().GetFuel(generator), Is.EqualTo(0f));
        });

        // The engineer has up to four minutes; pathfinding runs on a per-tick budget, so a busy CI runner needs the slack.
        var fuelled = false;
        for (var i = 0; i < 48 && !fuelled; i++)
        {
            await RunTicks(150);
            await Server.WaitAssertion(() =>
            {
                fuelled = Server.System<Content.Server.Power.Generator.GeneratorSystem>().GetFuel(generator) > 0f;
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(fuelled, Is.True, "The engineer refuels a generator that ran dry.");
            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>A fighter with no engineer aboard tops its own generator up when it runs dry, while its crew live.</summary>
    [Test]
    public async Task FighterWithoutAnEngineerRefuelsItself()
    {
        EntityUid encounter = default, ship = default, generator = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryFighterProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-35000, -35000), MapData.MapId), out encounter), Is.True);
            var state = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["fighter"];
            ship = state.Grid;
            Assert.That(state.AutoRefuel, Is.True, "A pilot and gunner alone see to their own fuel.");

            var crew = SEntMan.EntityQueryEnumerator<WGCrewEngineerComponent, TransformComponent>();
            while (crew.MoveNext(out _, out _, out var xform))
                Assert.That(xform.GridUid, Is.Not.EqualTo(ship), "No engineer is posted where the roles leave no room.");

            var generators = SEntMan.EntityQueryEnumerator<Content.Shared.Power.Generator.FuelGeneratorComponent, TransformComponent>();
            while (generators.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == ship)
                    generator = uid;
            }
            Assert.That(generator, Is.Not.EqualTo(default(EntityUid)));
            Server.System<Content.Server.Power.Generator.GeneratorSystem>().EmptyGenerator(generator);
        });
        // The encounter polls every five seconds.
        await RunTicks(300);
        await Server.WaitAssertion(() =>
        {
            var generators = Server.System<Content.Server.Power.Generator.GeneratorSystem>();
            Assert.That(generators.GetFuel(generator), Is.GreaterThan(0f), "The generator is topped up by itself.");
            Assert.That(SEntMan.GetComponent<Content.Shared.Power.Generator.FuelGeneratorComponent>(generator).On, Is.True);
            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>A faction's crew carry its names and its accent: a Union crew are Russians with Russian names.</summary>
    [Test]
    public async Task FactionCrewCarryItsNamesAndAccent()
    {
        EntityUid encounter = default, ship = default;
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var prototype = prototypes.Index<WGEncounterPrototype>(WGTestStoryUnionProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(-37000, -37000), MapData.MapId), out encounter), Is.True);
            ship = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["ship"].Grid;
            var surnames = prototypes.Index<Content.Shared.Dataset.DatasetPrototype>(WGNamesUsspLastProto).Values;

            var crew = 0;
            var members = SEntMan.EntityQueryEnumerator<WGCrewComponent, TransformComponent>();
            while (members.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != ship)
                    continue;
                crew++;
                Assert.That(SEntMan.HasComponent<Content.Server.Speech.Components.RussianAccentComponent>(uid), Is.True, "A Union crewman speaks with a Russian accent.");
                var name = SEntMan.GetComponent<MetaDataComponent>(uid).EntityName;
                Assert.That(surnames.Any(surname => name.EndsWith(surname)), Is.True, $"A Union crewman has a Russian surname: {name}");

                var inventory = Server.System<Content.Shared.Inventory.InventorySystem>();
                Assert.That(inventory.TryGetSlotEntity(uid, "ears", out var headset) && SEntMan.GetComponent<MetaDataComponent>(headset.Value).EntityPrototype?.ID == "WGCrewHeadsetUssp",
                    Is.True, "A Union crewman wears a Union headset.");
                Assert.That(inventory.TryGetSlotEntity(uid, "id", out var card), Is.True, "And carries a card.");
                var id = SEntMan.GetComponent<Content.Shared.Access.Components.IdCardComponent>(card!.Value);
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(card.Value).EntityPrototype?.ID, Does.StartWith("USSP"), "A Union service card.");
                Assert.That(id.FullName, Is.Not.Null.And.Not.Empty);
                Assert.That(name, Does.EndWith(id.FullName!), "The card carries his name without the title.");
                Assert.That(id.LocalizedJobTitle, Is.Not.Null.And.Not.Empty, "And his post.");
            }
            Assert.That(crew, Is.GreaterThan(0));
            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// An encounter ship inside another's zone, of a company that zone minds, makes enemies of the two. The two navies
    /// leave each other alone until war has been declared.
    /// </summary>
    [Test]
    public async Task EncounterShipsInEachOthersZonesTurnHostile()
    {
        EntityUid first = default, second = default, host = default;
        await Server.WaitAssertion(() =>
        {
            // The war level lives on the sector service entity, which a station's host component brings up.
            host = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            SEntMan.AddComponent<Content.Server._NF.SectorServices.StationSectorServiceHostComponent>(host);

            var system = Server.System<WGEncounterSystem>();
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            Assert.That(system.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestStoryIffProto), new MapCoordinates(new Vector2(37000, 37000), MapData.MapId), out first), Is.True);
            Assert.That(system.TrySpawn(prototypes.Index<WGEncounterPrototype>(WGTestStoryRivalProto), new MapCoordinates(new Vector2(37500, 37000), MapData.MapId), out second), Is.True);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var alerts = Server.System<WGCrewAlertSystem>();
            var patrol = SEntMan.GetComponent<WGEncounterComponent>(first).Ships["patrol"];
            var rival = SEntMan.GetComponent<WGEncounterComponent>(second).Ships["rival"];
            Assert.That(alerts.GetHostileShips(patrol.Grid, patrol.Group), Is.Empty, "Under the ceasefire the patrol lets the other navy's ship be.");
            Assert.That(alerts.GetHostileShips(rival.Grid, rival.Group), Is.Empty, "And is let be in turn.");
            Server.System<Content.Server._Mono.AlertLevel.WarLevelSystem>().SetLevel(true);
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            var alerts = Server.System<WGCrewAlertSystem>();
            var patrol = SEntMan.GetComponent<WGEncounterComponent>(first).Ships["patrol"];
            var rival = SEntMan.GetComponent<WGEncounterComponent>(second).Ships["rival"];
            Assert.That(alerts.GetHostileShips(patrol.Grid, patrol.Group), Does.Contain(rival.Grid), "Once war is declared the patrol takes the enemy ship in its zone for a threat.");
            Assert.That(alerts.GetHostileShips(rival.Grid, rival.Group), Does.Contain(patrol.Grid), "And is one to it in turn.");
            Server.System<WGEncounterSystem>().End(first);
            Server.System<WGEncounterSystem>().End(second);
            SEntMan.DeleteEntity(host);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// A hulk has no crew and stays in its encounter all the same. Its chief leaves the ship's papers when he dies, a
    /// claimed ship is worth a fraction of its parts, and once released it outlives the encounter.
    /// </summary>
    [Test]
    public async Task DwellerHulkIsClaimedAndKept()
    {
        EntityUid encounter = default, hulk = default, king = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryHulkProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(39000, 39000), MapData.MapId), out encounter), Is.True);
            hulk = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["hulk"].Grid;
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(encounter).Resolution, Is.Null, "A hulk with no crew is not a destroyed ship.");
            var crew = SEntMan.EntityQueryEnumerator<Content.Server._WG.NpcCrew.Components.WGCrewComponent, TransformComponent>();
            while (crew.MoveNext(out _, out _, out var xform))
                Assert.That(xform.GridUid, Is.Not.EqualTo(hulk), "Nobody crews a hulk.");

            var dwellers = 0;
            var mobs = SEntMan.EntityQueryEnumerator<Content.Shared.NPC.Components.NpcFactionMemberComponent, TransformComponent>();
            while (mobs.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid != hulk)
                    continue;
                dwellers++;
                if (SEntMan.HasComponent<Content.Server._WG.Encounters.Components.WGSalvageClaimDropComponent>(uid))
                    king = uid;
            }
            Assert.That(dwellers, Is.EqualTo(4), "Three dwellers and their king.");
            Assert.That(king, Is.Not.EqualTo(default(EntityUid)));
            Server.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(king, Content.Shared.Mobs.MobState.Dead);
        });
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            var found = false;
            var claims = SEntMan.EntityQueryEnumerator<Content.Server._WG.Encounters.Components.WGSalvageClaimComponent>();
            while (claims.MoveNext(out var uid, out var claim))
            {
                if (claim.Ship != hulk)
                    continue;
                found = true;
                Assert.That(Server.System<WGSalvageClaimSystem>().TryClaim((uid, claim), SEntMan.GetEntity(Player), out var reason), Is.False);
                Assert.That(reason, Is.EqualTo("wg-salvage-claim-not-aboard"));
            }
            Assert.That(found, Is.True, "The king leaves the ship's papers.");

            var pricing = Server.System<Content.Server.Cargo.Systems.PricingSystem>();
            var worth = pricing.AppraiseGrid(hulk);
            Assert.That(worth, Is.GreaterThan(0));
            SEntMan.EnsureComponent<Content.Server._WG.Encounters.Components.WGSalvagedShipComponent>(hulk);
            Assert.That(pricing.AppraiseGrid(hulk), Is.EqualTo(worth * 0.25).Within(1.0), "A claimed hulk sells for a quarter.");

            Server.System<WGEncounterSystem>().Release(hulk);
            Assert.That(SEntMan.HasComponent<WGEncounterGridComponent>(hulk), Is.False);
            Assert.That(SEntMan.GetComponent<WGEncounterComponent>(encounter).Resolution, Is.EqualTo(WGEncounterResolution.Completed));
        });
        await RunTicks(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(hulk), Is.True, "A claimed ship is not cleaned up with its encounter.");
            SEntMan.DeleteEntity(hulk);
        });
        await RunTicks(10);
    }

    /// <summary>
    /// Every hull an encounter may pick loads and takes a crew. One that can't would fail the spawn each time it is
    /// drawn, and the encounter would only ever be seen in its other hulls.
    /// </summary>
    [Test]
    public async Task EveryEncounterHullSpawnsAndTakesACrew()
    {
        var hulls = new SortedSet<string>();
        var derelicts = new HashSet<string>();
        await Server.WaitPost(() =>
        {
            foreach (var prototype in Server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<WGEncounterPrototype>())
            {
                if (prototype.ID.StartsWith("WGTest"))
                    continue;

                foreach (var ship in prototype.Ships)
                {
                    var named = ship.Vessels.Select(vessel => vessel.Id).ToList();
                    if (ship.Vessel is { } single)
                        named.Add(single.Id);
                    hulls.UnionWith(named);
                    if (ship.Derelict != null)
                        derelicts.UnionWith(named);
                }
            }
        });

        var failures = new List<string>();
        var x = 60000f;
        foreach (var hull in hulls)
        {
            x += 600f;
            var at = new Vector2(x, 60000f);
            await Server.WaitPost(() =>
            {
                var prototypes = Server.ResolveDependency<IPrototypeManager>();
                if (!prototypes.TryIndex<Content.Shared._NF.Shipyard.Prototypes.VesselPrototype>(hull, out var vessel)
                    || !Server.System<Content.Server._WG.Administration.Systems.AdminVesselSpawnSystem>()
                        .TrySpawnVessel(vessel, MapData.MapId, at, null, out var grid))
                {
                    failures.Add($"{hull}: does not load");
                    return;
                }

                // A hulk needs free deck, not posts.
                var crewable = Server.System<WGCrewSetupSystem>().Plan(grid.Value, 1, false).Count > 0;
                var deck = Server.System<WGCrewPlannerSystem>().HoldTiles(grid.Value, 4).Count > 0;
                if (!crewable && !derelicts.Contains(hull) || !deck)
                    failures.Add($"{hull}: {(deck ? "no crew posts" : "no free deck")}");
                SEntMan.DeleteEntity(grid.Value);
            });
            await RunTicks(2);
        }

        Assert.That(failures, Is.Empty, string.Join("; ", failures));
    }

    /// <summary>A ship that strays past its encounter's leash breaks off and flies back before it takes up its fight again.</summary>
    [Test]
    public async Task StrayingShipIsCalledBack()
    {
        EntityUid encounter = default, stray = default;
        await Server.WaitAssertion(() =>
        {
            var prototype = Server.ResolveDependency<IPrototypeManager>().Index<WGEncounterPrototype>(WGTestStoryLeashProto);
            Assert.That(Server.System<WGEncounterSystem>().TrySpawn(prototype, new MapCoordinates(new Vector2(45000, 45000), MapData.MapId), out encounter), Is.True);
            stray = SEntMan.GetComponent<WGEncounterComponent>(encounter).Ships["one"].Grid;
        });
        await RunTicks(150);
        await Server.WaitAssertion(() =>
        {
            Assert.That(First(), Is.EqualTo(WGCrewObjectiveKind.Attack), "Inside the leash it fights.");
            Server.System<SharedTransformSystem>().SetCoordinates(stray, new EntityCoordinates(MapData.MapUid, new Vector2(48000, 45000)));
        });
        // Over the 5 s poll that recalls it: 150 ticks is exactly one poll and can land just before it.
        await RunTicks(180);
        await Server.WaitAssertion(() =>
        {
            Assert.That(First(), Is.EqualTo(WGCrewObjectiveKind.GoTo), "Past it, it is sent back first.");
            Assert.That(Orders(), Has.Count.EqualTo(2), "And its fight is still waiting for it.");
            Server.System<WGEncounterSystem>().End(encounter);
        });
        await RunTicks(10);

        List<WGCrewObjective> Orders()
        {
            var net = SEntMan.GetNetEntity(stray);
            return Server.System<WGCrewObjectiveSystem>().Snapshot().First(crew => crew.Grid == net).Objectives;
        }

        WGCrewObjectiveKind First() => Orders()[0].Kind;
    }

    /// <summary>
    /// A mixed lot crate packs one thing from each of its lists, whatever it rolls: nothing is left out for want of
    /// room, and two crates of a kind are seldom alike.
    /// </summary>
    [Test]
    public async Task MixedLotCratesPackEveryRoll()
    {
        var lots = new List<string>();
        await Server.WaitPost(() =>
        {
            foreach (var prototype in Server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<EntityPrototype>())
            {
                if (prototype.ID.StartsWith("WGCrateLot"))
                    lots.Add(prototype.ID);
            }
        });
        Assert.That(lots, Has.Count.GreaterThanOrEqualTo(8));

        foreach (var lot in lots)
        {
            var crates = new List<EntityUid>();
            await Server.WaitPost(() =>
            {
                for (var i = 0; i < 12; i++)
                    crates.Add(SEntMan.SpawnEntity(lot, new EntityCoordinates(MapData.MapUid, new Vector2(70000 + i * 3, 70000))));
            });
            await RunTicks(3);
            await Server.WaitAssertion(() =>
            {
                var slots = Server.ResolveDependency<IPrototypeManager>().Index<EntityPrototype>(lot).Components.TryGetComponent("StorageFill", out var fill)
                    ? ((Content.Shared.Storage.Components.StorageFillComponent) fill).Contents.Select(entry => entry.GroupId).Distinct().Count()
                    : 0;
                var packings = new HashSet<string>();
                foreach (var crate in crates)
                {
                    var held = Server.System<Robust.Shared.Containers.SharedContainerSystem>()
                        .GetContainer(crate, "entity_storage").ContainedEntities
                        .Select(item => SEntMan.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID).OrderBy(id => id).ToList();
                    Assert.That(held, Has.Count.EqualTo(slots), $"{lot} left something out.");
                    packings.Add(string.Join(",", held));
                    SEntMan.DeleteEntity(crate);
                }

                Assert.That(packings, Has.Count.GreaterThan(6), $"{lot} packs the same things too often.");
            });
        }
    }
}
