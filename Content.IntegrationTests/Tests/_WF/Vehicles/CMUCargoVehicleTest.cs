#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.Storage.Components;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Bison;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Actions;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Explosion.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared.SSDIndicator;
using Content.Shared.StationRecords;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>
/// The Bison remote cargo carrier: pairing a controller, driving by remote through a mind visit, getting the body back
/// whenever the link breaks, the cargo bay, the self-destruct, and the carrier on decks that move, jump and end.
/// </summary>
[TestFixture]
public sealed class CMUCargoVehicleTest
{
    private const string CarrierId = "CMUCargoCarrier";
    private const string ControllerId = "CMUCargoCarrierController";
    private const string WreckId = "CMUCargoCarrierWreck";
    private const string CrateId = "CrateGenericSteel";
    private const string LockerId = "ClosetTool";

    /// <summary>The first tile past the end of the deck in the edge test.</summary>
    private const int EdgeX = 20;

    /// <summary>What can end remote control before the pilot asks.</summary>
    public enum Cause
    {
        ReturnAction,
        CarrierDeleted,
        ControllerDeleted,
        ControllerDropped,
        ControllerTaken,
        BodyKilled,
        BodyDeleted,
        CarrierChangedMap,
        BodyChangedMap,
    }

    /// <summary>A carrier on a rig with its pilot's body, controller and mind, once remote control has begun.</summary>
    private sealed class Remote
    {
        public required VehicleRig Rig;
        public EntityUid Controller;
        public EntityUid Mind;

        public EntityUid Carrier => Rig.Vehicle;
        public EntityUid Pilot => Rig.Driver;
    }

    private static int Ticks(IGameTiming timing, double seconds) => (int) Math.Ceiling(seconds * timing.TickRate);

    private static void HoldCarrier(VehicleRig rig, MoveButtons buttons)
    {
        rig.Pair.Server.EntMan.GetComponent<InputMoverComponent>(rig.Vehicle).HeldMoveButtons = buttons;
    }

    private static int CountPrototype(IEntityManager entities, string prototype)
    {
        var count = 0;
        var query = entities.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var metadata))
        {
            if (metadata.EntityPrototype?.ID == prototype)
                count++;
        }

        return count;
    }

    private static async Task<VehicleRig> BuildRig(TestPair pair, int maxX = 40, float degrees = 0f)
    {
        return await VehicleRigs.Build(pair, maxX: maxX, degrees: degrees, proto: CarrierId, facingRight: true);
    }

    /// <summary>Hands the pilot a controller, pairs it with the carrier and starts remote control; a player's session owns the mind if one is given.</summary>
    private static async Task<Remote> StartRemote(VehicleRig rig, ICommonSession? player = null)
    {
        var server = rig.Pair.Server;
        var remote = new Remote { Rig = rig };

        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var hands = server.System<SharedHandsSystem>();
            var minds = server.System<SharedMindSystem>();

            remote.Controller = entities.SpawnEntity(ControllerId, new EntityCoordinates(rig.Grid, 0.5f, 0.5f));
            Assert.That(hands.TryPickupAnyHand(rig.Driver, remote.Controller, checkActionBlocker: false), Is.True);

            remote.Mind = player == null ? minds.CreateMind(null).Owner : minds.GetOrCreateMind(player.UserId).Owner;
            minds.TransferTo(remote.Mind, rig.Driver);
            if (player != null)
                Assert.That(player.AttachedEntity, Is.EqualTo(rig.Driver), "Test setup: the player should be in the pilot's body.");

            var pairing = new InteractUsingEvent(rig.Driver, remote.Controller, rig.Vehicle, entities.GetComponent<TransformComponent>(rig.Vehicle).Coordinates);
            entities.EventBus.RaiseLocalEvent(rig.Vehicle, pairing);
            Assert.That(pairing.Handled, Is.True, "The controller did not pair.");
            Assert.That(entities.GetComponent<CMUCargoVehicleControllerComponent>(remote.Controller).LinkedVehicle, Is.EqualTo(rig.Vehicle));
            Assert.That(entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle).Controller, Is.EqualTo(remote.Controller));

            var use = new UseInHandEvent(rig.Driver);
            entities.EventBus.RaiseLocalEvent(remote.Controller, use);
            Assert.That(use.Handled, Is.True);
        });

        await server.WaitRunTicks(10);
        return remote;
    }

    /// <summary>The actions on the carrier that the visit grants; a connected player's carrier holds others too.</summary>
    private static List<EntityUid> BisonActions(IEntityManager entities, EntityUid carrier)
    {
        var found = new List<EntityUid>();
        foreach (var action in entities.GetComponent<ActionsComponent>(carrier).Actions)
        {
            if (entities.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID.StartsWith("CMUActionCargoVehicle") == true)
                found.Add(action);
        }

        return found;
    }

    private static void AssertControlling(Remote remote)
    {
        var entities = remote.Rig.Pair.Server.EntMan;
        var mind = entities.GetComponent<MindComponent>(remote.Mind);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mind.VisitingEntity, Is.EqualTo(remote.Carrier), "The pilot's mind is not visiting the carrier.");
            Assert.That(entities.HasComponent<CMUCargoVehicleControlSessionComponent>(remote.Carrier), Is.True);
            Assert.That(entities.HasComponent<CMUCargoVehicleRemotePilotComponent>(remote.Pilot), Is.True);
            Assert.That(entities.HasComponent<SSDIndicatorComponent>(remote.Pilot), Is.False, "A pilot at their post is not shown as asleep.");
            Assert.That(entities.GetComponent<RMCVehicleComponent>(remote.Carrier).Operator, Is.EqualTo(remote.Carrier));
            Assert.That(BisonActions(entities, remote.Carrier), Has.Count.EqualTo(3));
        }
    }

    /// <summary>The pilot is back in their own body, with no trace of the visit left on either side.</summary>
    private static void AssertReturned(Remote remote, bool bodyAlive, bool carrierAlive, HashSet<EntityUid>? grantedActions = null)
    {
        var entities = remote.Rig.Pair.Server.EntMan;
        var mind = entities.GetComponent<MindComponent>(remote.Mind);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mind.VisitingEntity, Is.Null, "The pilot's mind is still visiting.");
            Assert.That(mind.OwnedEntity, Is.Not.EqualTo(remote.Carrier), "The pilot's mind was left owning the carrier.");

            if (bodyAlive)
            {
                Assert.That(mind.OwnedEntity, Is.EqualTo(remote.Pilot), "The pilot did not get their body back.");
                Assert.That(entities.HasComponent<CMUCargoVehicleRemotePilotComponent>(remote.Pilot), Is.False);
                Assert.That(entities.TryGetComponent(remote.Pilot, out SSDIndicatorComponent? ssd), Is.True, "The SSD indicator was not given back.");
                Assert.That(ssd!.IsSSD, Is.EqualTo(!entities.HasComponent<ActorComponent>(remote.Pilot)), "The indicator should show asleep only when no player is in the body.");
            }

            if (carrierAlive)
            {
                Assert.That(entities.HasComponent<CMUCargoVehicleControlSessionComponent>(remote.Carrier), Is.False);
                Assert.That(entities.HasComponent<VisitingMindComponent>(remote.Carrier), Is.False);
                Assert.That(BisonActions(entities, remote.Carrier), Is.Empty, "The visit's actions were left on the carrier.");
                Assert.That(entities.GetComponent<RMCVehicleComponent>(remote.Carrier).Operator, Is.EqualTo(remote.Carrier), "The carrier lost its operator link.");
            }

            if (grantedActions != null)
            {
                foreach (var action in grantedActions)
                {
                    Assert.That(entities.EntityExists(action) && !entities.IsQueuedForDeletion(action), Is.False, "A granted action was not deleted.");
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // The Bison itself
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A controller pairs by being used on the carrier, and used in hand it puts the pilot's mind in the carrier, which
    /// is its own operator and drives like any vehicle while the pilot's body stays put.
    /// </summary>
    [Test]
    public async Task ARemotePilotDrivesTheCarrierWhileTheirBodyStaysPut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var remote = await StartRemote(rig);

        var carrierStart = Vector2.Zero;
        var bodyStart = Vector2.Zero;
        await server.WaitAssertion(() =>
        {
            AssertControlling(remote);
            carrierStart = VehicleRigs.LocalPosition(rig);
            bodyStart = server.EntMan.GetComponent<TransformComponent>(rig.Driver).LocalPosition;
        });

        await server.WaitPost(() => HoldCarrier(rig, MoveButtons.Up));
        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var carrierNow = VehicleRigs.LocalPosition(rig);
            var bodyNow = server.EntMan.GetComponent<TransformComponent>(rig.Driver).LocalPosition;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(carrierNow.X, Is.GreaterThan(carrierStart.X + 5f), "The carrier did not drive.");
                Assert.That(carrierNow.Y, Is.EqualTo(carrierStart.Y).Within(0.05f));
                Assert.That(VehicleRigs.Gap(bodyNow, bodyStart), Is.LessThan(0.01f), "The pilot's body moved.");
                Assert.That(server.EntMan.GetComponent<TransformComponent>(rig.Vehicle).GridUid, Is.EqualTo(rig.Grid));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The controller works across maps, and dropping it ends the visit and gives the SSD indicator back.</summary>
    [Test]
    public async Task ControllerWorksAcrossMapsAndDroppingItReturnsTheMindToItsBody()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var otherMap = await pair.CreateTestMap();
        var remote = new Remote { Rig = rig };

        // The carrier is on one map and the pilot on the other before control begins.
        await server.WaitPost(() =>
        {
            server.System<SharedTransformSystem>().SetCoordinates(rig.Driver, otherMap.GridCoords);
        });
        remote = await StartRemote(rig);

        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<TransformComponent>(rig.Vehicle).MapID, Is.Not.EqualTo(server.EntMan.GetComponent<TransformComponent>(rig.Driver).MapID));
            AssertControlling(remote);
            Assert.That(server.System<SharedHandsSystem>().TryDrop(remote.Pilot, remote.Controller, checkActionBlocker: false), Is.True);
        });

        await server.WaitRunTicks(10);
        await server.WaitAssertion(() => AssertReturned(remote, bodyAlive: true, carrierAlive: true));

        await pair.CleanReturnAsync();
    }

    /// <summary>However remote control ends, the pilot's mind is back in their own body and the carrier is left clean.</summary>
    [Test]
    public async Task ThePilotAlwaysGetsTheirBodyBack([Values] Cause cause)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var remote = await StartRemote(rig);
        var granted = new HashSet<EntityUid>();
        var otherMap = EntityUid.Invalid;
        var otherMapId = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            AssertControlling(remote);
            foreach (var action in BisonActions(server.EntMan, remote.Carrier))
            {
                granted.Add(action);
            }

            var entities = server.EntMan;
            var xforms = server.System<SharedTransformSystem>();
            var session = entities.GetComponent<CMUCargoVehicleControlSessionComponent>(remote.Carrier);

            switch (cause)
            {
                case Cause.ReturnAction:
                    var action = session.ReturnAction!.Value;
                    var ev = new CMUCargoVehicleReturnActionEvent
                    {
                        Performer = remote.Carrier,
                        Action = new Entity<BaseActionComponent>(action, entities.GetComponent<InstantActionComponent>(action)),
                    };
                    entities.EventBus.RaiseLocalEvent(remote.Carrier, ev);
                    Assert.That(ev.Handled, Is.True);
                    break;
                case Cause.CarrierDeleted:
                    entities.DeleteEntity(remote.Carrier);
                    break;
                case Cause.ControllerDeleted:
                    entities.DeleteEntity(remote.Controller);
                    break;
                case Cause.ControllerDropped:
                    Assert.That(server.System<SharedHandsSystem>().TryDrop(remote.Pilot, remote.Controller, checkActionBlocker: false), Is.True);
                    break;
                case Cause.ControllerTaken:
                    // Someone else takes it, or it is stowed: either way it leaves the pilot's hand.
                    var crate = entities.SpawnEntity(CrateId, new EntityCoordinates(rig.Grid, 4.5f, 0.5f));
                    var storage = entities.GetComponent<EntityStorageComponent>(crate);
                    Assert.That(server.System<SharedContainerSystem>().Insert(remote.Controller, storage.Contents, force: true), Is.True);
                    break;
                case Cause.BodyKilled:
                    server.System<MobStateSystem>().ChangeMobState(remote.Pilot, MobState.Dead);
                    break;
                case Cause.BodyDeleted:
                    entities.DeleteEntity(remote.Pilot);
                    break;
                case Cause.CarrierChangedMap:
                    otherMap = server.System<SharedMapSystem>().CreateMap(out otherMapId);
                    xforms.SetCoordinates(rig.Grid, new EntityCoordinates(otherMap, new Vector2(100f, 100f)));
                    Assert.That(entities.GetComponent<TransformComponent>(remote.Carrier).MapID, Is.EqualTo(otherMapId));
                    break;
                case Cause.BodyChangedMap:
                    otherMap = server.System<SharedMapSystem>().CreateMap(out otherMapId);
                    xforms.SetCoordinates(remote.Pilot, new EntityCoordinates(otherMap, new Vector2(10f, 10f)));
                    Assert.That(entities.GetComponent<TransformComponent>(remote.Pilot).MapID, Is.EqualTo(otherMapId));
                    break;
            }
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var bodyAlive = cause != Cause.BodyDeleted && cause != Cause.BodyKilled;
            var carrierAlive = cause != Cause.CarrierDeleted;

            if (cause == Cause.BodyKilled)
            {
                // A dead body still owns the mind; it is back in it.
                var mind = server.EntMan.GetComponent<MindComponent>(remote.Mind);
                Assert.That(mind.VisitingEntity, Is.Null);
                Assert.That(mind.OwnedEntity, Is.EqualTo(remote.Pilot));
                Assert.That(server.EntMan.HasComponent<CMUCargoVehicleControlSessionComponent>(remote.Carrier), Is.False);
                Assert.That(BisonActions(server.EntMan, remote.Carrier), Is.Empty);
                return;
            }

            AssertReturned(remote, bodyAlive, carrierAlive, granted);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>How a connected player's visit ends.</summary>
    public enum PlayerEnd
    {
        ReturnAction,
        Disconnect,
        BodyDeleted,
    }

    /// <summary>With a real session the player is attached to the carrier for the visit, and is never left there.</summary>
    [Test]
    public async Task APlayerFollowsTheVisitToTheCarrierAndNeverStaysThere([Values] PlayerEnd end)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var player = pair.Player!;
        var remote = await StartRemote(rig, player);

        await server.WaitAssertion(() =>
        {
            AssertControlling(remote);
            Assert.That(player.AttachedEntity, Is.EqualTo(rig.Vehicle), "The player was not attached to the carrier.");

            switch (end)
            {
                case PlayerEnd.ReturnAction:
                    var session = entities.GetComponent<CMUCargoVehicleControlSessionComponent>(rig.Vehicle);
                    var action = session.ReturnAction!.Value;
                    var ev = new CMUCargoVehicleReturnActionEvent
                    {
                        Performer = rig.Vehicle,
                        Action = new Entity<BaseActionComponent>(action, entities.GetComponent<InstantActionComponent>(action)),
                    };
                    entities.EventBus.RaiseLocalEvent(rig.Vehicle, ev);
                    break;
                case PlayerEnd.Disconnect:
                    // What a dropped connection does to the carrier: its player is detached.
                    server.PlayerMan.SetAttachedEntity(player, null);
                    break;
                case PlayerEnd.BodyDeleted:
                    entities.DeleteEntity(rig.Driver);
                    break;
            }
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            var mind = entities.GetComponent<MindComponent>(remote.Mind);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(player.AttachedEntity, Is.Not.EqualTo(rig.Vehicle), "The player was left in the carrier.");
                Assert.That(mind.VisitingEntity, Is.Null);
                Assert.That(mind.OwnedEntity, Is.Not.EqualTo(rig.Vehicle));
                Assert.That(entities.HasComponent<CMUCargoVehicleControlSessionComponent>(rig.Vehicle), Is.False);
                Assert.That(entities.TryGetComponent(rig.Vehicle, out MindContainerComponent? container) && container.HasMind, Is.False, "The carrier kept a mind.");

                if (end == PlayerEnd.ReturnAction)
                {
                    Assert.That(player.AttachedEntity, Is.EqualTo(rig.Driver), "The player did not get their body back.");
                    Assert.That(entities.GetComponent<SSDIndicatorComponent>(rig.Driver).IsSSD, Is.False);
                }

                if (end == PlayerEnd.Disconnect)
                    Assert.That(entities.GetComponent<SSDIndicatorComponent>(rig.Driver).IsSSD, Is.True, "A body whose player left should show as asleep.");

                if (end != PlayerEnd.BodyDeleted)
                    Assert.That(mind.OwnedEntity, Is.EqualTo(rig.Driver));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The carrier is its own operator. The access search follows every entity a vehicle adds as its operator, so adding
    /// itself sent the search round on the carrier for ever: driving into any door with a reader crashed the server. It
    /// meets doors as a thing with no access: one that asks for none opens, any other holds it.
    /// </summary>
    [TestCase("Airlock", true)]
    [TestCase("AirlockEngineeringLocked", false)]
    public async Task TheCarrierMeetsDoorsWithoutSearchingItsOwnAccessForEver(string prototype, bool opens)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var doorX = 12.5f;
        var column = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            for (var y = 0; y < 6; y++)
            {
                var door = entities.SpawnEntity(prototype, new EntityCoordinates(rig.Grid, doorX, y + 0.5f));
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(door, false);
                column.Add(door);
            }
        });
        await server.WaitRunTicks(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(column.All(d => entities.GetComponent<AirlockComponent>(d).Powered), Is.True, "Test setup: the doors should be powered.");

            // The search itself: it returns, and the carrier has the access of a thing with none.
            Assert.That(server.System<AccessReaderSystem>().IsAllowed(rig.Vehicle, column[2]), Is.EqualTo(opens));
        });

        await server.WaitPost(() => HoldCarrier(rig, MoveButtons.Up));
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var x = VehicleRigs.LocalPosition(rig).X;

            if (opens)
            {
                var logged = column.Select(d => entities.GetComponent<AccessReaderComponent>(d).AccessLog.Count).ToList();
                TestContext.Out.WriteLine($"Access log entries per door: {string.Join(", ", logged)}");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(x, Is.GreaterThan(doorX + 2f), "The carrier should have driven through a door that needs no access.");
                    Assert.That(logged.Max(), Is.LessThanOrEqualTo(3), "A door was asked for the carrier's access over and over.");
                }
            }
            else
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(x + 0.95f, Is.LessThan(doorX), "The carrier went through a door it has no access to.");
                    Assert.That(column.Any(d => entities.GetComponent<DoorComponent>(d).State is DoorState.Open or DoorState.Opening), Is.False, "The carrier opened a locked door.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Whatever kind of door asks, the search for what could give the carrier access finds the carrier alone and returns,
    /// and the door gets the answer it would give a thing with no access. The firelock asks from a handler of its own, and
    /// the windoors have readers of their own kinds.
    /// </summary>
    [TestCase("Airlock")]
    [TestCase("AirlockEngineeringLocked")]
    [TestCase("Firelock")]
    [TestCase("Windoor")]
    [TestCase("WindoorSecure")]
    public async Task EveryKindOfDoorGetsAnAnswerForTheCarrierAndTheSearchEnds(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var door = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            door = entities.SpawnEntity(prototype, new EntityCoordinates(rig.Grid, 12.5f, 2.5f));
            server.System<SharedPowerReceiverSystem>().SetNeedsPower(door, false);
        });
        await server.WaitRunTicks(40);

        await server.WaitAssertion(() =>
        {
            var access = server.System<AccessReaderSystem>();
            var reader = entities.GetComponent<AccessReaderComponent>(door);
            var empty = access.IsAllowed(new List<ProtoId<AccessLevelPrototype>>(), new List<StationRecordKey>(), door, reader);

            // A stack overflow cannot be caught: if any of these recursed, the test host would die and not fail.
            var sources = access.FindPotentialAccessItems(rig.Vehicle);
            var asked = access.IsAllowed(rig.Vehicle, door);
            var opened = server.System<SharedDoorSystem>().CanOpen(door, user: rig.Vehicle);
            TestContext.Out.WriteLine($"{prototype}: allowed {asked}, can open {opened}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(sources, Is.EquivalentTo(new[] { rig.Vehicle }), "The carrier's access sources should be the carrier alone.");
                Assert.That(access.FindAccessTags(rig.Vehicle), Is.Empty, "The carrier has no access of its own.");
                Assert.That(access.FindStationRecordKeys(rig.Vehicle, out _), Is.False, "The carrier has no crew record.");
                Assert.That(asked, Is.EqualTo(empty), "The door gave the carrier a different answer from the one it gives a thing with no access.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Control that a jump did not touch goes on: a drifting, turning deck changes nothing.</summary>
    [Test]
    public async Task ARemoteSessionSurvivesADeckThatDriftsAndTurns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var remote = await StartRemote(rig);

        await SetShipMoving(rig);
        await server.WaitPost(() => HoldCarrier(rig, MoveButtons.Up));
        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            AssertControlling(remote);
            Assert.That(VehicleRigs.LocalPosition(rig).X, Is.GreaterThan(7f), "The carrier stopped driving on the moving deck.");
        });

        await pair.CleanReturnAsync();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Cargo
    // ------------------------------------------------------------------------------------------------------------

    private static Verb? FindVerb(TestPair pair, EntityUid target, EntityUid user, string key)
    {
        var server = pair.Server;
        var text = server.ResolveDependency<ILocalizationManager>().GetString(key);
        return server.System<SharedVerbSystem>().GetLocalVerbs(target, user, typeof(AlternativeVerb), force: true).FirstOrDefault(v => v.Text == text);
    }

    private static void RunVerb(TestPair pair, EntityUid target, EntityUid user, string key)
    {
        var verb = FindVerb(pair, target, user, key);
        Assert.That(verb, Is.Not.Null, $"\"{key}\" was not offered.");
        pair.Server.System<SharedVerbSystem>().ExecuteVerb(verb!, user, target, forced: true);
    }

    /// <summary>A pulled crate is loaded into the open bay and unloaded again; lockers and a closed bay are refused.</summary>
    [Test]
    public async Task APulledCrateIsLoadedAndUnloadedThroughTheOpenBay()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid crate = default;
        EntityUid locker = default;
        EntityUid user = default;

        await server.WaitPost(() =>
        {
            var carrierPosition = entities.GetComponent<TransformComponent>(rig.Vehicle).LocalPosition;
            user = entities.SpawnEntity("MobHuman", new EntityCoordinates(rig.Grid, carrierPosition + new Vector2(1.2f, 0f)));
            crate = entities.SpawnEntity(CrateId, new EntityCoordinates(rig.Grid, carrierPosition + new Vector2(1.2f, 1.2f)));
            locker = entities.SpawnEntity(LockerId, new EntityCoordinates(rig.Grid, carrierPosition + new Vector2(-1.2f, 1.2f)));
        });
        await server.WaitRunTicks(5);

        // Closed bay: nothing to load into.
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<PullingSystem>().TryStartPull(user, crate), Is.True, "Test setup: the user could not pull the crate.");
            {
                var used = server.System<SharedInteractionSystem>().TryGetUsedEntity(user, out var usedEntity) ? entities.ToPrettyString(usedEntity!.Value) : "nothing";
                var offered = string.Join(", ", server.System<SharedVerbSystem>().GetLocalVerbs(rig.Vehicle, user, typeof(AlternativeVerb), force: true).Select(v => v.Text));
                Assert.That(FindVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-open"), Is.Not.Null, $"used: {used}; offered: [{offered}]");
            }
            Assert.That(FindVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-load"), Is.Null, "A crate was offered for loading into a closed bay.");
            RunVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-open");
            Assert.That(entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle).BayOpen, Is.True);
            Assert.That(FindVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-load"), Is.Not.Null, "A pulled crate was not offered for loading.");
        });

        await server.WaitAssertion(() => RunVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-load"));
        await server.WaitRunTicks(Ticks(server.Timing, 2.5));

        await server.WaitAssertion(() =>
        {
            var cargo = entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(cargo.CargoContainer!.ContainedEntity, Is.EqualTo(crate), "The crate was not loaded.");
                Assert.That(entities.GetComponent<PullableComponent>(crate).BeingPulled, Is.False, "The loaded crate is still being pulled.");
            }

            // Loaded: the bay holds one crate, and nothing is offered but unloading.
            Assert.That(FindVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-load"), Is.Null);
            Assert.That(FindVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-unload"), Is.Not.Null);
            RunVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-unload");
        });
        await server.WaitRunTicks(Ticks(server.Timing, 2.5));

        await server.WaitAssertion(() =>
        {
            var cargo = entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle);
            var userPosition = server.System<SharedTransformSystem>().GetWorldPosition(user);
            var cratePosition = server.System<SharedTransformSystem>().GetWorldPosition(crate);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cargo.CargoContainer!.ContainedEntity, Is.Null, "The crate was not unloaded.");
                Assert.That(VehicleRigs.Gap(userPosition, cratePosition), Is.LessThan(1.5f), "The crate was not put down by the user.");
            }

            // A locker is no crate.
            Assert.That(server.System<PullingSystem>().TryStartPull(user, locker), Is.True, "Test setup: the user could not pull the locker.");
            Assert.That(FindVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-load"), Is.Null, "A locker was offered for loading.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The bay stays shut to cargo while the carrier is moving.</summary>
    [Test]
    public async Task ACarrierThatIsMovingTakesNoCargo()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid crate = default;
        EntityUid user = default;

        await server.WaitPost(() =>
        {
            entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle).BayOpen = true;
            HoldCarrier(rig, MoveButtons.Up);
            var carrierPosition = entities.GetComponent<TransformComponent>(rig.Vehicle).LocalPosition;
            user = entities.SpawnEntity("MobHuman", new EntityCoordinates(rig.Grid, carrierPosition + new Vector2(0f, 1.5f)));
            crate = entities.SpawnEntity(CrateId, new EntityCoordinates(rig.Grid, carrierPosition + new Vector2(0f, 2.2f)));
        });
        await server.WaitRunTicks(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(VehicleRigs.Mover(rig).IsMoving, Is.True, "Test setup: the carrier should be moving.");
            Assert.That(server.System<PullingSystem>().TryStartPull(user, crate), Is.True);
            Assert.That(FindVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-load"), Is.Null, "A moving carrier was offered cargo.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A user buckled to a chair is parented to it. A crate put down at the user's own coordinates would be parented to the
    /// chair as well and go with it; it lands on the deck, and outlives the chair.
    /// </summary>
    [Test]
    public async Task ACrateUnloadedByAUserInAChairLandsOnTheDeckAndNotOnTheChair()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid crate = default;
        EntityUid chair = default;
        EntityUid user = default;

        await server.WaitPost(() =>
        {
            var carrierPosition = entities.GetComponent<TransformComponent>(rig.Vehicle).LocalPosition;
            var seat = new EntityCoordinates(rig.Grid, carrierPosition + new Vector2(1.2f, 0f));
            user = entities.SpawnEntity("MobHuman", seat);
            chair = entities.SpawnEntity("Chair", seat);
            crate = entities.SpawnEntity(CrateId, new EntityCoordinates(rig.Grid, carrierPosition + new Vector2(0f, 1.2f)));

            var cargo = entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle);
            cargo.BayOpen = true;
            Assert.That(server.System<SharedContainerSystem>().Insert(crate, cargo.CargoContainer!, force: true), Is.True, "Test setup: the crate could not be loaded.");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(user, user, chair), Is.True, "Test setup: the user could not sit in the chair.");
            Assert.That(entities.GetComponent<TransformComponent>(user).ParentUid, Is.EqualTo(chair), "Test setup: a buckled user is parented to the chair.");
            RunVerb(pair, rig.Vehicle, user, "cmu-cargo-vehicle-verb-unload");
        });
        await server.WaitRunTicks(Ticks(server.Timing, 2.5));

        await server.WaitAssertion(() =>
        {
            var cargo = entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cargo.CargoContainer!.ContainedEntity, Is.Null, "The crate was not unloaded.");
                Assert.That(entities.GetComponent<TransformComponent>(crate).ParentUid, Is.EqualTo(rig.Grid), "The crate was put down under the chair.");
            }

            entities.DeleteEntity(chair);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() => Assert.That(entities.EntityExists(crate) && !entities.IsQueuedForDeletion(crate), Is.True, "The crate went with the chair."));

        await pair.CleanReturnAsync();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Self-destruct
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Arming by hand keeps the carrier drivable for five seconds; then it blows up, throws its cargo clear, leaves a
    /// wreck, debris and fire, harms no floor, and the pilot is in their own body.
    /// </summary>
    [Test]
    public async Task SelfDestructReturnsThePilotToTheirBodyAndDestroysTheCarrier()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var remote = await StartRemote(rig);

        EntityUid crate = default;
        EntityUid item = default;
        var wrecksBefore = 0;
        var tilesBefore = 0;
        var debrisBefore = new Dictionary<string, int>();
        var granted = new HashSet<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var containers = server.System<SharedContainerSystem>();
            var cargo = entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle);
            crate = entities.SpawnEntity(CrateId, new EntityCoordinates(rig.Grid, 4.5f, 4.5f));
            item = entities.SpawnEntity("Crowbar", new EntityCoordinates(rig.Grid, 4.5f, 4.5f));
            var storage = entities.GetComponent<EntityStorageComponent>(crate);
            Assert.That(containers.Insert(item, storage.Contents, force: true), Is.True);
            Assert.That(containers.Insert(crate, cargo.CargoContainer!, force: true), Is.True);

            wrecksBefore = CountPrototype(entities, WreckId);
            foreach (var debris in cargo.DebrisPrototypes)
            {
                debrisBefore[debris.Id] = CountPrototype(entities, debris.Id);
            }

            tilesBefore = server.System<SharedMapSystem>().GetAllTiles(rig.Grid, entities.GetComponent<MapGridComponent>(rig.Grid)).Count();
            foreach (var action in BisonActions(entities, rig.Vehicle))
            {
                granted.Add(action);
            }

            var session = entities.GetComponent<CMUCargoVehicleControlSessionComponent>(rig.Vehicle);
            var destruct = session.SelfDestructAction!.Value;
            var ev = new CMUCargoVehicleSelfDestructActionEvent
            {
                Performer = rig.Vehicle,
                Action = new Entity<BaseActionComponent>(destruct, entities.GetComponent<InstantActionComponent>(destruct)),
            };
            entities.EventBus.RaiseLocalEvent(rig.Vehicle, ev);
            Assert.That(ev.Handled, Is.True);
            Assert.That(cargo.ArmingMode, Is.EqualTo(CMUCargoVehicleArmingMode.Manual));
            Assert.That(entities.HasComponent<ActiveTimerTriggerComponent>(rig.Vehicle), Is.True);

            var canRun = new VehicleCanRunEvent((rig.Vehicle, entities.GetComponent<RMCVehicleComponent>(rig.Vehicle)));
            entities.EventBus.RaiseLocalEvent(rig.Vehicle, ref canRun);
            Assert.That(canRun.CanRun, Is.True, "A manually armed carrier should still drive.");
        });

        // Still the pilot's for a while: it drives and the link holds.
        await server.WaitPost(() => HoldCarrier(rig, MoveButtons.Up));
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            AssertControlling(remote);
            Assert.That(VehicleRigs.LocalPosition(rig).X, Is.GreaterThan(4f), "The armed carrier did not drive.");
        });
        await server.WaitPost(() => HoldCarrier(rig, MoveButtons.None));
        await server.WaitRunTicks(30);

        // A plain wall right beside the carrier, which the blast must not take down.
        EntityUid wall = default;
        await server.WaitPost(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);
            wall = entities.SpawnEntity("WallSolid", new EntityCoordinates(rig.Grid, MathF.Floor(position.X) + 0.5f, MathF.Floor(position.Y) + 1.5f));
        });

        // The moment the carrier goes, the wreck, debris and fire are there.
        var gone = false;
        var firesAtDetonation = 0;
        for (var i = 0; i < Ticks(server.Timing, 8) && !gone; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() =>
            {
                if (!entities.EntityExists(rig.Vehicle) || entities.IsQueuedForDeletion(rig.Vehicle))
                {
                    gone = true;
                    firesAtDetonation = CountFires(entities);
                }
            });
        }

        Assert.That(gone, Is.True, "The carrier never blew up.");
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var debrisSpawned = debrisBefore.Sum(pair => CountPrototype(entities, pair.Key) - pair.Value);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(entities.EntityExists(rig.Vehicle), Is.False, "The carrier survived its own explosion.");
                Assert.That(CountPrototype(entities, WreckId), Is.EqualTo(wrecksBefore + 1));
                Assert.That(debrisSpawned, Is.InRange(4, 7));
                Assert.That(firesAtDetonation, Is.GreaterThan(0), "The blast left no fire.");
                Assert.That(entities.EntityExists(item) && !entities.IsQueuedForDeletion(item), Is.True, "The cargo's contents were lost.");
                Assert.That(entities.GetComponent<TransformComponent>(item).ParentUid, Is.Not.EqualTo(crate));
                Assert.That(!entities.EntityExists(crate) || entities.IsQueuedForDeletion(crate), Is.True, "The crate was left standing.");
                Assert.That(server.System<SharedMapSystem>().GetAllTiles(rig.Grid, entities.GetComponent<MapGridComponent>(rig.Grid)).Count(), Is.EqualTo(tilesBefore), "The blast broke the floor.");
                Assert.That(entities.EntityExists(wall) && !entities.IsQueuedForDeletion(wall), Is.True, "The blast took down a wall next to it.");

                var mind = entities.GetComponent<MindComponent>(remote.Mind);
                Assert.That(mind.VisitingEntity, Is.Null);
                Assert.That(mind.OwnedEntity, Is.EqualTo(remote.Pilot), "The pilot did not get their body back.");
                Assert.That(entities.HasComponent<CMUCargoVehicleRemotePilotComponent>(remote.Pilot), Is.False);
                Assert.That(entities.HasComponent<SSDIndicatorComponent>(remote.Pilot), Is.True);
                Assert.That(server.System<MobStateSystem>().IsAlive(remote.Pilot), Is.True, "The pilot was in the blast.");
                Assert.That(entities.GetComponent<CMUCargoVehicleControllerComponent>(remote.Controller).LinkedVehicle, Is.Null, "The controller still points at a dead carrier.");

                foreach (var action in granted)
                {
                    Assert.That(entities.EntityExists(action), Is.False, "A granted action was not deleted.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    private static int CountFires(IEntityManager entities)
    {
        var count = 0;
        var query = entities.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var metadata))
        {
            if (metadata.EntityPrototype?.ID.StartsWith("ESTileFire") == true)
                count++;
        }

        return count;
    }

    /// <summary>300 damage arms the self-destruct on its own, ends control and stops the carrier, and nothing disarms it.</summary>
    [Test]
    public async Task CriticalDamageArmsTheCarrierStopsItAndCannotBeReversed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var entities = server.EntMan;
        var remote = await StartRemote(rig);
        var granted = new HashSet<EntityUid>();

        await server.WaitAssertion(() =>
        {
            AssertControlling(remote);
            foreach (var action in BisonActions(entities, rig.Vehicle))
            {
                granted.Add(action);
            }

            server.System<DamageableSystem>().TryChangeDamage(
                rig.Vehicle,
                new DamageSpecifier { DamageDict = { ["Blunt"] = 300 } },
                ignoreResistances: true);
        });

        await server.WaitRunTicks(10);
        await server.WaitPost(() => HoldCarrier(rig, MoveButtons.Up));
        var stopped = Vector2.Zero;
        await server.WaitAssertion(() => stopped = VehicleRigs.LocalPosition(rig));
        await server.WaitRunTicks(60);

        await server.WaitAssertion(() =>
        {
            var cargo = entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle);
            var canRun = new VehicleCanRunEvent((rig.Vehicle, entities.GetComponent<RMCVehicleComponent>(rig.Vehicle)));
            entities.EventBus.RaiseLocalEvent(rig.Vehicle, ref canRun);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(cargo.ArmingMode, Is.EqualTo(CMUCargoVehicleArmingMode.Automatic));
                Assert.That(entities.HasComponent<ActiveTimerTriggerComponent>(rig.Vehicle), Is.True);
                Assert.That(canRun.CanRun, Is.False, "An automatically armed carrier should not drive.");
                Assert.That(VehicleRigs.LocalPosition(rig).X, Is.EqualTo(stopped.X).Within(0.05f), "The armed carrier drove away.");
            }

            AssertReturned(remote, bodyAlive: true, carrierAlive: true, granted);

            server.System<DamageableSystem>().TryChangeDamage(
                rig.Vehicle,
                new DamageSpecifier { DamageDict = { ["Blunt"] = -300 } },
                ignoreResistances: true);
            Assert.That(cargo.ArmingMode, Is.EqualTo(CMUCargoVehicleArmingMode.Automatic));
            Assert.That(entities.HasComponent<ActiveTimerTriggerComponent>(rig.Vehicle), Is.True);
        });

        // Once armed, the carrier cannot be paired, controlled or opened again.
        await server.WaitAssertion(() =>
        {
            var use = new UseInHandEvent(remote.Pilot);
            entities.EventBus.RaiseLocalEvent(remote.Controller, use);
            Assert.That(use.Handled, Is.True);
        });
        await server.WaitRunTicks(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<MindComponent>(remote.Mind).VisitingEntity, Is.Null, "An armed carrier took a new pilot.");
            Assert.That(entities.HasComponent<CMUCargoVehicleControlSessionComponent>(rig.Vehicle), Is.False);
        });

        // A pending detonation must not outlive the test.
        await server.WaitPost(() => entities.DeleteEntity(rig.Vehicle));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The pilot's player is attached to the carrier for the visit, so what the self-destruct action tells the pilot has to
    /// be told to the carrier: told to the body, which no player is attached to, nobody sees it.
    /// </summary>
    [Test]
    public async Task ArmingTheSelfDestructTwiceTellsThePilotItIsAlreadyArmed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var rig = await BuildRig(pair);
        var server = pair.Server;
        var client = pair.Client;
        var entities = server.EntMan;
        var player = pair.Player!;
        await StartRemote(rig, player);
        var localization = client.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.That(player.AttachedEntity, Is.EqualTo(rig.Vehicle), "Test setup: the player should be attached to the carrier.");
            Assert.That(entities.HasComponent<ActorComponent>(rig.Driver), Is.False, "Test setup: the body should have no player.");
            Assert.That(RaiseSelfDestruct(entities, rig.Vehicle), Is.True);
            Assert.That(entities.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle).ArmingMode, Is.EqualTo(CMUCargoVehicleArmingMode.Manual));
        });

        // The popup that arming makes for everyone nearby shows that popups reach this player's client at all.
        Assert.That(
            await PopupSeen(pair, localization.GetString("cmu-cargo-vehicle-manual-armed"), 20),
            Is.True,
            "Test setup: the armed popup never reached the player.");

        await server.WaitAssertion(() => Assert.That(RaiseSelfDestruct(entities, rig.Vehicle), Is.True));

        Assert.That(
            await PopupSeen(pair, localization.GetString("cmu-cargo-vehicle-already-armed"), 20),
            Is.True,
            "The pilot's player was not told the carrier is already armed.");

        // A pending detonation must not outlive the test.
        await server.WaitPost(() => entities.DeleteEntity(rig.Vehicle));
        await pair.CleanReturnAsync();
    }

    /// <summary>Whether the client shows a popup with this text within a number of ticks; it is checked after every tick, since popups expire.</summary>
    private static async Task<bool> PopupSeen(TestPair pair, string text, int ticks)
    {
        var client = pair.Client;
        for (var i = 0; i < ticks; i++)
        {
            await pair.RunTicksSync(1);

            var seen = false;
            await client.WaitPost(() => seen = client.System<Content.Client.Popups.PopupSystem>().WorldLabels.Any(label => label.Text.Contains(text)));
            if (seen)
                return true;
        }

        return false;
    }

    /// <summary>Raises the self-destruct action the visit grants, as the pilot pressing it would.</summary>
    private static bool RaiseSelfDestruct(IEntityManager entities, EntityUid carrier)
    {
        var session = entities.GetComponent<CMUCargoVehicleControlSessionComponent>(carrier);
        var destruct = session.SelfDestructAction!.Value;
        var ev = new CMUCargoVehicleSelfDestructActionEvent
        {
            Performer = carrier,
            Action = new Entity<BaseActionComponent>(destruct, entities.GetComponent<InstantActionComponent>(destruct)),
        };
        entities.EventBus.RaiseLocalEvent(carrier, ev);
        return ev.Handled;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Falling through z-levels
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Driving off an edge with a level below drops the carrier like any vehicle. It has no frame, so its body takes the
    /// landing, and the pilot, whose body stays on the level above, keeps the carrier: a fall is not a jump.
    /// </summary>
    [Test]
    public async Task ARemotePilotKeepsACarrierThatDrivesOffAnEdgeToTheLevelBelow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var levels = await FallLevelRigs.Build(pair, CarrierId, facingRight: true);
        var server = pair.Server;
        var entities = server.EntMan;
        var remote = await StartRemote(levels.Rig);

        try
        {
            await server.WaitAssertion(() => AssertControlling(remote));
            await server.WaitPost(() => HoldCarrier(levels.Rig, MoveButtons.Up));

            Assert.That(await FallLevelRigs.RunUntilLanded(levels, 500), Is.True, "The carrier never dropped to the lower map.");

            await server.WaitAssertion(() =>
            {
                var xform = entities.GetComponent<TransformComponent>(levels.Vehicle);
                var cargo = entities.GetComponent<CMUCargoVehicleComponent>(levels.Vehicle);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(xform.GridUid, Is.EqualTo(levels.LowerGrid), "The carrier did not land on the lower grid.");
                    Assert.That(entities.HasComponent<HardpointIntegrityComponent>(levels.Vehicle), Is.False, "Test setup: the carrier has no frame.");

                    // Weak weight class: 10% of the 300 damage that arms it.
                    Assert.That((float) entities.GetComponent<DamageableComponent>(levels.Vehicle).TotalDamage, Is.EqualTo(30f).Within(0.5f), "The carrier's body took the wrong landing damage.");
                    Assert.That(cargo.ArmingMode, Is.EqualTo(CMUCargoVehicleArmingMode.None), "A small fall armed the self-destruct.");
                    Assert.That(VehicleRigs.Mover(levels.Rig).CurrentSpeed, Is.Zero, "A landing should end the carrier's speed.");
                    Assert.That(FallLevelRigs.MapOf(levels, levels.Rig.Driver), Is.EqualTo(levels.Rig.Map.MapId), "The pilot's body should still be on the level above.");
                }

                AssertControlling(remote);
            });

            // The session check runs four times a second: the link holds through several, and the carrier still drives.
            var landedX = 0f;
            await server.WaitAssertion(() => landedX = VehicleRigs.WorldPosition(levels.Rig).X);
            await server.WaitRunTicks(Ticks(server.Timing, 1.5));

            await server.WaitAssertion(() =>
            {
                AssertControlling(remote);
                Assert.That(VehicleRigs.WorldPosition(levels.Rig).X, Is.GreaterThan(landedX + 2f), "The carrier could not be driven after landing.");
                Assert.That(FallLevelRigs.MapOf(levels, levels.Vehicle), Is.EqualTo(levels.LowerMap));
            });
        }
        finally
        {
            await server.WaitPost(() => HoldCarrier(levels.Rig, MoveButtons.None));
            await FallLevelRigs.Teardown(levels);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A landing that takes the carrier past 300 damage arms the self-destruct as any damage does: control ends, the pilot is
    /// back in their body, the carrier stops, and it blows up and leaves its wreck.
    /// </summary>
    [Test]
    public async Task ACarrierTheFallWrecksArmsItselfAndSendsItsPilotHome()
    {
        await using var pair = await PoolManager.GetServerClient();
        var levels = await FallLevelRigs.Build(pair, CarrierId, facingRight: true);
        var server = pair.Server;
        var entities = server.EntMan;
        var remote = await StartRemote(levels.Rig);
        var granted = new HashSet<EntityUid>();
        var wrecksBefore = 0;

        try
        {
            await server.WaitAssertion(() =>
            {
                AssertControlling(remote);
                granted.UnionWith(BisonActions(entities, levels.Vehicle));
                wrecksBefore = CountPrototype(entities, WreckId);

                // Ten short of arming it: the fall's own 30 is what tips it over.
                server.System<DamageableSystem>().TryChangeDamage(
                    levels.Vehicle,
                    new DamageSpecifier { DamageDict = { ["Blunt"] = 290 } },
                    ignoreResistances: true);
                Assert.That(entities.GetComponent<CMUCargoVehicleComponent>(levels.Vehicle).ArmingMode, Is.EqualTo(CMUCargoVehicleArmingMode.None), "Test setup: 290 damage should not arm it.");
            });

            await server.WaitPost(() => HoldCarrier(levels.Rig, MoveButtons.Up));
            Assert.That(await FallLevelRigs.RunUntilLanded(levels, 500), Is.True, "The carrier never dropped to the lower map.");
            await server.WaitRunTicks(10);

            var stopped = Vector2.Zero;
            await server.WaitAssertion(() =>
            {
                var cargo = entities.GetComponent<CMUCargoVehicleComponent>(levels.Vehicle);
                stopped = VehicleRigs.LocalPosition(levels.Rig);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(cargo.ArmingMode, Is.EqualTo(CMUCargoVehicleArmingMode.Automatic), "The fall did not arm the carrier.");
                    Assert.That(entities.HasComponent<ActiveTimerTriggerComponent>(levels.Vehicle), Is.True);
                    Assert.That(entities.GetComponent<DamageableComponent>(levels.Vehicle).TotalDamage.Float(), Is.GreaterThanOrEqualTo(300f));
                }

                AssertReturned(remote, bodyAlive: true, carrierAlive: true, granted);
            });

            // It does not drive on, whatever is still held down.
            await server.WaitRunTicks(30);
            await server.WaitAssertion(() => Assert.That(VehicleRigs.LocalPosition(levels.Rig).X, Is.EqualTo(stopped.X).Within(0.05f), "The wrecked carrier drove away."));

            // Then it goes the way a wrecked carrier goes.
            var gone = false;
            for (var i = 0; i < Ticks(server.Timing, 8) && !gone; i++)
            {
                await server.WaitRunTicks(1);
                await server.WaitPost(() => gone = !entities.EntityExists(levels.Vehicle) || entities.IsQueuedForDeletion(levels.Vehicle));
            }

            Assert.That(gone, Is.True, "The wrecked carrier never blew up.");
            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(CountPrototype(entities, WreckId), Is.EqualTo(wrecksBefore + 1), "The carrier left no wreck.");
                    Assert.That(entities.GetComponent<MindComponent>(remote.Mind).OwnedEntity, Is.EqualTo(remote.Pilot), "The pilot did not keep their body.");
                    Assert.That(server.System<MobStateSystem>().IsAlive(remote.Pilot), Is.True);
                }
            });
        }
        finally
        {
            // The carrier is gone if the test got as far as its explosion.
            await server.WaitPost(() =>
            {
                if (entities.EntityExists(levels.Vehicle))
                    entities.DeleteEntity(levels.Vehicle);
            });
            await FallLevelRigs.Teardown(levels);
        }

        await pair.CleanReturnAsync();
    }

    // ------------------------------------------------------------------------------------------------------------
    // Decks that move, jump and end
    // ------------------------------------------------------------------------------------------------------------

    private static readonly Vector2 ShipVelocity = new(1.5f, 0.75f);
    private const float ShipSpin = 0.3f;

    /// <summary>Gives the rig's grid a body that drifts and spins on its own with nothing to slow it.</summary>
    private static async Task SetShipMoving(VehicleRig rig)
    {
        await rig.Pair.Server.WaitPost(() =>
        {
            var physics = rig.Pair.Server.System<SharedPhysicsSystem>();
            var body = rig.Pair.Server.EntMan.GetComponent<PhysicsComponent>(rig.Grid);

            physics.SetBodyType(rig.Grid, BodyType.Dynamic);
            physics.SetSleepingAllowed(rig.Grid, body, false);
            physics.SetLinearDamping(rig.Grid, body, 0f);
            physics.SetAngularDamping(rig.Grid, body, 0f);
            physics.SetLinearVelocity(rig.Grid, ShipVelocity, body: body);
            physics.SetAngularVelocity(rig.Grid, ShipSpin, body: body);
            physics.WakeBody(rig.Grid, body: body);
        });
    }

    private static (Vector2 Position, Angle Rotation) GridPose(VehicleRig rig)
    {
        var xforms = rig.Pair.Server.System<SharedTransformSystem>();
        return (xforms.GetWorldPosition(rig.Grid), xforms.GetWorldRotation(rig.Grid));
    }

    /// <summary>A parked carrier on a ship that drifts and turns keeps its place and facing on the deck, and takes no velocity from it.</summary>
    [Test]
    public async Task AParkedCarrierStaysPutOnAMovingShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, proto: CarrierId, facingRight: true);
        var server = pair.Server;
        var entities = server.EntMan;

        var start = Vector2.Zero;
        var startRotation = Angle.Zero;
        var gridStart = (Position: Vector2.Zero, Rotation: Angle.Zero);

        await server.WaitAssertion(() =>
        {
            start = VehicleRigs.LocalPosition(rig);
            startRotation = entities.GetComponent<TransformComponent>(rig.Vehicle).LocalRotation;
            gridStart = GridPose(rig);
        });

        await SetShipMoving(rig);
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var gridNow = GridPose(rig);
            var xform = entities.GetComponent<TransformComponent>(rig.Vehicle);
            var body = entities.GetComponent<PhysicsComponent>(rig.Vehicle);

            using (Assert.EnterMultipleScope())
            {
                Assert.That((gridNow.Position - gridStart.Position).Length(), Is.GreaterThan(3f), "Test setup: the ship should have drifted.");
                Assert.That(Math.Abs((gridNow.Rotation - gridStart.Rotation).Theta), Is.GreaterThan(0.5f), "Test setup: the ship should have turned.");

                Assert.That(xform.GridUid, Is.EqualTo(rig.Grid));
                Assert.That(VehicleRigs.Gap(xform.LocalPosition, start), Is.LessThan(0.001f), "The carrier slid across the deck.");
                Assert.That(xform.LocalRotation.Theta, Is.EqualTo(startRotation.Theta).Within(0.001f), "The carrier turned on the deck.");
                Assert.That(body.LinearVelocity, Is.EqualTo(Vector2.Zero), "The ship's motion must not become the carrier's velocity.");
                Assert.That(entities.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.EqualTo(rig.Vehicle));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Driven by remote, the carrier gets as far along a moving, turning deck as along one at rest.</summary>
    [Test]
    public async Task DrivingTheCarrierOnAMovingShipMatchesDrivingAtRest()
    {
        var atRest = Vector2.Zero;
        var moving = Vector2.Zero;

        await using (var pair = await PoolManager.GetServerClient())
        {
            var rig = await BuildRig(pair, maxX: 80);
            var remote = await StartRemote(rig);
            var start = Vector2.Zero;
            await pair.Server.WaitAssertion(() => start = VehicleRigs.LocalPosition(rig));
            await pair.Server.WaitPost(() => HoldCarrier(rig, MoveButtons.Up));
            await pair.Server.WaitRunTicks(120);
            await pair.Server.WaitAssertion(() =>
            {
                atRest = VehicleRigs.LocalPosition(rig) - start;
                AssertControlling(remote);
            });

            await pair.CleanReturnAsync();
        }

        await using (var pair = await PoolManager.GetServerClient())
        {
            var rig = await BuildRig(pair, maxX: 80);
            var remote = await StartRemote(rig);
            var start = Vector2.Zero;
            await pair.Server.WaitAssertion(() => start = VehicleRigs.LocalPosition(rig));
            await SetShipMoving(rig);
            await pair.Server.WaitPost(() => HoldCarrier(rig, MoveButtons.Up));
            await pair.Server.WaitRunTicks(120);
            await pair.Server.WaitAssertion(() =>
            {
                moving = VehicleRigs.LocalPosition(rig) - start;
                AssertControlling(remote);
            });

            await pair.CleanReturnAsync();
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(atRest.X, Is.GreaterThan(7f), "Test setup: the carrier should have covered some ground.");
            Assert.That(moving.X, Is.EqualTo(atRest.X).Within(0.05f), "The ship's drift changed how far the carrier got along the deck.");
            Assert.That(moving.Y, Is.EqualTo(atRest.Y).Within(0.05f), "The ship's spin pushed the carrier sideways on the deck.");
        }
    }

    /// <summary>A ship that jumps takes a parked carrier with it, in the same place, and it still drives afterwards.</summary>
    [Test]
    public async Task AShipChangingMapKeepsAParkedCarrierWhereItWas()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await VehicleRigs.Build(pair, maxX: 80, proto: CarrierId, facingRight: true);
        var server = pair.Server;
        var entities = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var maps = server.System<SharedMapSystem>();

        var before = Vector2.Zero;
        var newMapId = MapId.Nullspace;

        await server.WaitAssertion(() =>
        {
            before = VehicleRigs.LocalPosition(rig);
            var newMap = maps.CreateMap(out newMapId);
            xforms.SetCoordinates(rig.Grid, new EntityCoordinates(newMap, new Vector2(100f, 100f)));
            xforms.SetLocalRotation(rig.Grid, Angle.FromDegrees(30f));

            var xform = entities.GetComponent<TransformComponent>(rig.Vehicle);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(xform.MapID, Is.EqualTo(newMapId), "The carrier did not travel with its grid.");
                Assert.That(xform.GridUid, Is.EqualTo(rig.Grid));
                Assert.That(VehicleRigs.Gap(xform.LocalPosition, before), Is.LessThan(0.001f), "The carrier moved on the deck.");
                Assert.That(VehicleRigs.Mover(rig).SyncedGrid, Is.EqualTo(rig.Grid));
                Assert.That(entities.GetComponent<RMCVehicleComponent>(rig.Vehicle).Operator, Is.EqualTo(rig.Vehicle), "The jump cost the carrier its operator link.");
            }
        });

        await server.WaitPost(() => HoldCarrier(rig, MoveButtons.Up));
        await server.WaitRunTicks(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(VehicleRigs.LocalPosition(rig).X, Is.GreaterThan(before.X + 5f), "The carrier stopped driving after the jump.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The carrier stops at the end of the deck like any vehicle, and the hit makes the carrier's own sound.</summary>
    [Test]
    public async Task TheCarrierIsRefusedAtADeckEdge()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await BuildRig(pair, maxX: EdgeX);
        var server = pair.Server;
        var probe = server.System<VehicleEventProbeSystem>();

        await server.WaitPost(() => HoldCarrier(rig, MoveButtons.Up));
        await server.WaitRunTicks(300);

        await server.WaitAssertion(() =>
        {
            var position = VehicleRigs.LocalPosition(rig);
            var xform = server.EntMan.GetComponent<TransformComponent>(rig.Vehicle);
            var cargo = server.EntMan.GetComponent<CMUCargoVehicleComponent>(rig.Vehicle);

            // The carrier is 1.9 long; facing along the deck, its front sits 0.95 ahead of its origin. The floor check
            // uses the footprint inset by 0.05 and the carrier stops on a 0.1 probe, so it may hang over by up to 0.15.
            using (Assert.EnterMultipleScope())
            {
                Assert.That(position.X + 0.95f, Is.LessThan(EdgeX + 0.2f), "The carrier drove off the deck.");
                Assert.That(position.X + 0.95f, Is.GreaterThan(EdgeX - 0.7f), "The carrier stopped well short of the edge.");
                Assert.That(xform.GridUid, Is.EqualTo(rig.Grid), "The carrier left the grid.");
                Assert.That(VehicleRigs.Mover(rig).CurrentSpeed, Is.Zero);
                Assert.That(
                    probe.Collisions.Any(c => c.Vehicle == rig.Vehicle && c.Event.Kind == VehicleCollisionKind.Crash && c.Event.Speed > cargo.CollisionSoundMinSpeed),
                    Is.True,
                    "Stopping at the edge should be reported as a crash.");
                Assert.That(cargo.NextCollisionSound, Is.GreaterThan(TimeSpan.Zero), "The hit did not make the carrier's sound.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
