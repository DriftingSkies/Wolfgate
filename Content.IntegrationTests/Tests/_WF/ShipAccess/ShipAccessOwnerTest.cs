#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._NF.SectorServices;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.ShipAccess;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.StationRecords.Systems;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Administration.Ert;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ShipAccess;

/// <summary>
/// Owners besides an ID card holding the deed: the voucher a ship was bought with, a player the admin vessel
/// spawner registers a ship to whatever body they are in, and each responder of an ERT with a ship.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipAccessServerSystem))]
public sealed class ShipAccessOwnerTest
{
    private const string DoorProto = "AirlockShuttle";
    private const string HumanProto = "MobHuman";
    private const string GhostProto = "MobObserver";
    private const string CardProto = "PassengerIDCard";
    private const string VoucherProto = "ShipVoucherFrontierService";
    private const string ErtVessel = "Arribane";
    private const string ErtOutfit = "ERTLeaderGear";

    /// <summary>An outfit with no ID card or PDA in it.</summary>
    private const string NoIdOutfit = "GladiatorGear";

    /// <summary>An outfit whose PDA holds no ID card, and one with something else in the ID slot.</summary>
    private const string EmptyPdaOutfit = "WFErtTestEmptyPdaGear";
    private const string BlockedSlotOutfit = "WFErtTestBlockedSlotGear";
    private const string EmptyPda = "ChameleonPDA";
    private const string BlockingItem = "Crowbar";

    [TestPrototypes]
    private const string Prototypes = @"
- type: startingGear
  id: WFErtTestEmptyPdaGear
  equipment:
    jumpsuit: ClothingUniformJumpsuitColorGrey
    id: ChameleonPDA

- type: startingGear
  id: WFErtTestBlockedSlotGear
  equipment:
    jumpsuit: ClothingUniformJumpsuitColorGrey
    id: Crowbar
";

    /// <summary>Access a faction ship's doors might be mapped with.</summary>
    private const string FactionAccess = "Pirate";

    /// <summary>An access group holding <see cref="FactionAccess"/>.</summary>
    private const string FactionGroup = "PDV";

    /// <summary>
    /// A captain who bought the ship with a voucher holds its deed while holding the voucher, and so may edit its
    /// access; their own card, taken at purchase, still opens its doors once the voucher is put away.
    /// </summary>
    [Test]
    public async Task VoucherHolderOwnsTheShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        EntityUid door = default, captain = default, captainCard = default, stranger = default, voucher = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            (captain, captainCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            (stranger, _) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            voucher = entMan.SpawnEntity(VoucherProto, map.GridCoords);
            ShipAccessTest.GiveDeed(entMan, voucher, grid);
            Assert.That(hands.TryPickupAnyHand(captain, voucher), Is.True, "Precondition: the captain holds the voucher.");
            entMan.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(grid, captain));
            access.SetLocked((grid, entMan.GetComponent<WFShipAccessComponent>(grid)), true);
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(access.IsOwner(captain, grid), Is.True, "Holding the voucher with the deed makes the captain the owner.");
                Assert.That(access.IsOwner(stranger, grid), Is.False);
                Assert.That(readers.IsAllowed(captain, door), Is.True, "The captain opens the locked ship.");
                Assert.That(readers.IsAllowed(stranger, door), Is.False, "A stranger does not.");
            });

            Assert.That(hands.TryDrop(captain, voucher), Is.True, "Precondition: the captain puts the voucher down.");
            Assert.Multiple(() =>
            {
                Assert.That(access.IsOwner(captain, grid), Is.True, "Their card carries an owner key, so they still edit its access.");
                Assert.That(readers.IsAllowed(captain, door), Is.True, "Their own card still opens the ship they bought.");
            });

            Assert.That(hands.TryDrop(captain, captainCard), Is.True, "Precondition: the captain drops their card.");
            Assert.That(hands.TryPickupAnyHand(stranger, captainCard), Is.True, "Precondition: the stranger picks it up.");
            Assert.That(access.IsOwner(stranger, grid), Is.True, "Whoever uses the owner's card is treated as the owner.");
            Assert.That(access.IsOwner(captain, grid), Is.False, "Without the card or the deed, the captain is not.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ship the admin vessel spawner registers to a ghost belongs to the player: they own it as a ghost, and once
    /// they play a body wearing a card with a crew record, that card opens its doors and lets them lock it. The
    /// client knows the player owns it, so the tab is editable.
    /// </summary>
    [Test]
    public async Task AdminSpawnRegistersAGhostToTheirAccount()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var ghost = await Attach(pair, session, GhostProto, map.GridCoords, "Ghost");

        EntityUid door = default, stranger = default;
        await server.WaitAssertion(() =>
        {
            door = entMan.SpawnEntity(DoorProto, map.GridCoords);
            (stranger, _) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);
            var vessel = server.ProtoMan.EnumeratePrototypes<VesselPrototype>().First(v => !v.Abstract);

            Assert.That(spawner.TryGetDeedCard(session, out _, out var reason), Is.False, "A ghost's card can't hold a deed.");
            Assert.That(reason, Is.EqualTo("cmd-spawnvessel-owner-ghost"));
            Assert.That(spawner.TryAssignOwner(grid, vessel, null, session), Is.True);

            var comp = entMan.GetComponent<WFShipAccessComponent>(grid);
            Assert.Multiple(() =>
            {
                Assert.That(comp.OwnerUsers, Does.Contain(session.UserId), "The ship is registered to the player's account.");
                Assert.That(access.IsOwner(ghost, grid), Is.True, "The ghost owns it.");
                Assert.That(comp.Locked, Is.False, "With no card to key the doors to, the ship is left unlocked.");
            });
        });

        var body = await Attach(pair, session, HumanProto, map.GridCoords, "Ada Vance");
        await server.WaitPost(() =>
        {
            var card = entMan.SpawnEntity(CardProto, map.GridCoords);
            entMan.EnsureComponent<StationRecordKeyStorageComponent>(card);
            entMan.System<StationRecordKeyStorageSystem>().AssignKey(card, new StationRecordKey(1, map.MapUid));
            Assert.That(entMan.System<InventorySystem>().TryEquip(body, card, "id", force: true), Is.True, "Precondition: the body wears the card.");
        });
        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            var ship = new Entity<WFShipAccessComponent>(grid, entMan.GetComponent<WFShipAccessComponent>(grid));
            Assert.That(access.CanLock(ship), Is.True, "The worn card gives the ship an owner key.");
            Assert.That(ship.Comp.Locked, Is.True, "The ship locked itself once the owner's card could open it.");
            Assert.Multiple(() =>
            {
                Assert.That(access.IsOwner(body, grid), Is.True, "The player owns the ship in their new body.");
                Assert.That(readers.IsAllowed(body, door), Is.True, "Their worn card opens the locked ship.");
                Assert.That(readers.IsAllowed(stranger, door), Is.False, "A stranger's does not.");
            });
        });
        await pair.RunTicksSync(10);

        var clientGrid = pair.ToClientUid(grid);
        await pair.Client.WaitAssertion(() =>
        {
            var local = pair.Client.Session!.AttachedEntity;
            Assert.That(local, Is.Not.Null, "Precondition: the client has its body.");
            Assert.That(pair.Client.EntMan.System<WFShipAccessSystem>().IsOwner(local!.Value, clientGrid), Is.True,
                "The client knows the ship is registered to its player.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// An ERT spawned with a ship registers each responder to it as they take their place, and gives them a crew
    /// record, so their card can be keyed to the ship's doors. The ship locks itself once the first responder's
    /// card can open it.
    /// </summary>
    [Test]
    public async Task ErtRespondersOwnTheirShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var readers = entMan.System<AccessReaderSystem>();
        var access = entMan.System<WFShipAccessServerSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        // Crew records live on the sector service, which only a station with a host makes.
        EntityUid? host = null;
        await server.WaitPost(() =>
        {
            if (entMan.System<SectorServiceSystem>().GetServiceEntity().Valid)
                return;

            host = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            entMan.AddComponent<StationSectorServiceHostComponent>(host.Value);
        });

        // The admin calls the team from out in space, clear of the test grid.
        await Attach(pair, session, GhostProto, new EntityCoordinates(map.MapUid, 300, 300), "Admin");

        EntityUid ship = default;
        uint role = default;
        await server.WaitAssertion(() =>
        {
            var config = new ErtConfig
            {
                TeamName = "Test Team",
                Members = 1,
                HasLeader = true,
                MemberOutfit = ErtOutfit,
                Vessel = ErtVessel,
            };
            Assert.That(entMan.System<ErtSystem>().TrySpawnTeam(session, config, out var message), Is.True, message);

            var ships = entMan.EntityQueryEnumerator<WFShipAccessComponent>();
            while (ships.MoveNext(out var uid, out var comp))
            {
                if (comp.OwnerName == "Test Team")
                    ship = uid;
            }

            Assert.That(ship, Is.Not.EqualTo(EntityUid.Invalid), "The team's ship has ship access.");
            Assert.That(entMan.GetComponent<WFShipAccessComponent>(ship).Locked, Is.False, "With nobody aboard yet there is no card to lock it to.");
            var slot = entMan.EntityQuery<WolfgateErtSpawnerComponent>().Single();
            role = entMan.GetComponent<GhostRoleComponent>(slot.Owner).Identifier;
        });

        await server.WaitAssertion(() =>
            Assert.That(entMan.System<GhostRoleSystem>().Takeover(session, role), Is.True, "The player takes the place."));
        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            var responder = session.AttachedEntity!.Value;
            var comp = entMan.GetComponent<WFShipAccessComponent>(ship);
            Assert.That(comp.OwnerUsers, Does.Contain(session.UserId), "The responder is registered to the team's ship.");

            Assert.That(entMan.System<WFShipAccessSystem>().TryGetWornCard(responder, out var card), Is.True, "Precondition: the responder wears an ID.");
            var key = entMan.GetComponent<StationRecordKeyStorageComponent>(card).Key;
            Assert.That(key, Is.Not.Null, "The responder's card has a crew record.");
            Assert.That(entMan.System<StationRecordsSystem>().TryGetRecord<GeneralStationRecord>(key!.Value, out _), Is.True);

            var door = FindDoor(entMan, ship);
            var (stranger, _) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ben Ortiz", 2);

            var owned = new Entity<WFShipAccessComponent>(ship, comp);
            Assert.That(comp.Locked, Is.True, "The ship locked itself once the responder's card could open it.");
            Assert.That(readers.IsAllowed(responder, door), Is.True, "The responder opens their locked ship.");

            // Unlocked, a faction ship's doors ask for the faction's access: the responder's card opens them
            // anyway, with none of that access on it.
            access.SetLocked(owned, false);
            entMan.System<SharedAccessSystem>().TrySetTags(card, new List<ProtoId<AccessLevelPrototype>>());
            Assert.That(readers.GetMainAccessReader(door, out var doorReader), Is.True);
            readers.SetAccesses(doorReader!.Value, doorReader.Value.Comp, new List<ProtoId<AccessLevelPrototype>> { FactionAccess });
            access.RefreshReader(door);
            Assert.Multiple(() =>
            {
                Assert.That(readers.IsAllowed(responder, door), Is.True, "The responder opens a faction door on the unlocked ship.");
                Assert.That(readers.IsAllowed(stranger, door), Is.False, "A stranger without the faction's access does not.");
            });

            Assert.That(access.CanLock(owned), Is.True, "The responder's card gives the ship an owner key.");
            access.SetLocked(owned, true);
            Assert.Multiple(() =>
            {
                Assert.That(access.IsOwner(responder, ship), Is.True, "The responder may edit the ship's access.");
                Assert.That(readers.IsAllowed(responder, door), Is.True, "The responder opens the locked ship.");
                Assert.That(readers.IsAllowed(stranger, door), Is.False, "A stranger does not.");
            });

            if (host != null)
                entMan.DeleteEntity(host.Value);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A responder whose outfit has no ID card is handed a plain one, worn in the ID slot where a ship's doors look
    /// for it, carrying only the access the admin chose. It goes into a PDA worn there, and takes the place of
    /// anything else the outfit put in the slot, which ends up in a hand.
    /// </summary>
    [TestCase(NoIdOutfit, null)]
    [TestCase(EmptyPdaOutfit, EmptyPda)]
    [TestCase(BlockedSlotOutfit, null)]
    public async Task ErtOutfitWithoutAnIdGetsOne(string outfit, string? wornProto)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        await Attach(pair, session, GhostProto, map.GridCoords, "Admin");

        uint role = default;
        await server.WaitAssertion(() =>
        {
            var config = new ErtConfig
            {
                TeamName = "Bare Team",
                Members = 1,
                HasLeader = true,
                MemberOutfit = outfit,
                AccessGroups = new List<string> { FactionGroup },
            };
            Assert.That(entMan.System<ErtSystem>().TrySpawnTeam(session, config, out var message), Is.True, message);
            var slot = entMan.EntityQuery<WolfgateErtSpawnerComponent>().Single();
            role = entMan.GetComponent<GhostRoleComponent>(slot.Owner).Identifier;
        });

        await server.WaitAssertion(() =>
            Assert.That(entMan.System<GhostRoleSystem>().Takeover(session, role), Is.True, "The player takes the place."));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var responder = session.AttachedEntity!.Value;
            Assert.That(entMan.System<WFShipAccessSystem>().TryGetWornCard(responder, out var card), Is.True, "The responder wears an ID the outfit did not have.");
            var tags = entMan.GetComponent<AccessComponent>(card).Tags;
            Assert.Multiple(() =>
            {
                Assert.That(tags, Does.Contain(new ProtoId<AccessLevelPrototype>(FactionAccess)), "The card carries the access the admin chose.");
                Assert.That(tags, Is.SubsetOf(server.ProtoMan.Index(new ProtoId<AccessGroupPrototype>(FactionGroup)).Tags), "And none of the plain card's own.");
            });

            Assert.That(entMan.System<InventorySystem>().TryGetSlotEntity(responder, "id", out var worn), Is.True);
            var wornId = entMan.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype?.ID;
            if (wornProto != null)
                Assert.That(wornId, Is.EqualTo(wornProto), "The card went into the PDA the outfit wears.");
            else
                Assert.That(worn.Value, Is.EqualTo(card), "The card itself is in the ID slot.");

            if (outfit == BlockedSlotOutfit)
            {
                var heldProtos = entMan.System<SharedHandsSystem>().EnumerateHeld(responder)
                    .Select(held => entMan.GetComponent<MetaDataComponent>(held).EntityPrototype?.ID);
                Assert.That(heldProtos, Does.Contain(BlockingItem), "What the outfit had in the ID slot moved to a hand.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Moves the player, with a new mind, into a named entity of the given prototype.</summary>
    private static async Task<EntityUid> Attach(TestPair pair, ICommonSession session, string proto, EntityCoordinates at, string name)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = entMan.SpawnEntity(proto, at);
            entMan.System<MetaDataSystem>().SetEntityName(body, name);
            var mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, body);
        });

        await pair.RunTicksSync(5);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the body.");
        return body;
    }

    /// <summary>An airlock on the grid whose reader the ship access writes.</summary>
    private static EntityUid FindDoor(IEntityManager entMan, EntityUid grid)
    {
        var readers = entMan.System<AccessReaderSystem>();
        var doors = entMan.EntityQueryEnumerator<DoorComponent, TransformComponent>();
        while (doors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && !entMan.HasComponent<FirelockComponent>(uid) && readers.GetMainAccessReader(uid, out _))
                return uid;
        }

        Assert.Fail("The ship has no airlock with an access reader.");
        return default;
    }
}
