#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.ShipAdmin;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.ShipAccess;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._NF.ShuttleRecords;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.Companies;
using Content.Server._WG.ShipAccess;
using Content.Server._WG.ShipAdmin;
using Content.Server.Administration.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared.Access.Components;
using Content.Shared.StationRecords;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Shuttles.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.ShipAdmin;

/// <summary>
/// The ship panel's ownership actions: a transfer moves the deed and every owner record without touching the lock,
/// mode or allow list; a target with no usable card is registered by account; hangar ships and offline players are
/// refused; owners can be added, removed and released, which locks the helm; the hull's faction follows the option.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipAdminSystem))]
public sealed class ShipAdminOwnershipTest
{
    private const string VesselProto = "Guppy";
    private const string GhostProto = "MobObserver";
    private const string HullFaction = "TSF";
    private const string CardFaction = "PDV";

    /// <summary>The company an ID with no faction names.</summary>
    private const string NoFaction = "None";

    [Test]
    public async Task TransferMovesEveryOwnerRecord()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WGShipAdminSystem>();
        var shipyard = entMan.System<ShipyardSystem>();
        var access = entMan.System<WGShipAccessServerSystem>();
        var records = entMan.System<ShuttleRecordsSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var phantom = new NetUserId(Guid.NewGuid());

        var ship = default(Ship);
        EntityUid ghost = default;
        var wasAdmin = false;
        WGShipAdminEuiState state = default!;

        await server.WaitAssertion(() =>
        {
            wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            ship = SpawnOwnedShip(pair, map, session);
            var grid = ship.Grid;
            var comp = entMan.GetComponent<WGShipAccessComponent>(grid);
            var netGrid = entMan.GetNetEntity(grid);

            Assert.That(access.AddOwnerUser(grid, phantom), Is.True, "Precondition: a second account is registered.");
            Assert.That(access.TryAddPerson((grid, comp), ship.Guest), Is.True, "Precondition: a guest is on the allow list.");
            var modeBefore = comp.Mode;
            var lockedBefore = comp.Locked;
            Assert.That(entMan.GetComponent<ShuttleDeedComponent>(grid).ShuttleOwner, Is.EqualTo("Ada Vance"));

            // The player now plays Ben, who carries a card without a deed.
            server.PlayerMan.SetAttachedEntity(session, ship.Ben);
            Assert.That(ships.Transfer(session, grid, new WGShipAdminTransferMessage(netGrid, session.UserId), out var result), Is.True, result);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ShuttleDeedComponent>(ship.AdaCard), Is.False, "The old card loses the deed.");
                Assert.That(entMan.TryGetComponent<ShuttleDeedComponent>(ship.BenCard, out var deed), Is.True, "The new card gains it.");
                Assert.That(deed?.ShuttleUid, Is.EqualTo(grid));
                Assert.That(deed?.DeedHolder, Is.EqualTo(ship.BenCard));
                Assert.That(entMan.GetComponent<ShuttleDeedComponent>(grid).ShuttleOwner, Is.EqualTo("Ben Ortiz"));
                Assert.That(entMan.GetComponent<ShipOwnershipComponent>(grid).OwnerUserId, Is.EqualTo(session.UserId));
                Assert.That(comp.OwnerUsers, Is.EquivalentTo(new[] { session.UserId }), "Every earlier account is unregistered.");
                Assert.That(comp.OwnerName, Is.EqualTo("Ben Ortiz"));
                Assert.That(records.TryGetRecord(netGrid, out var record), Is.True);
                Assert.That(record?.OwnerName, Is.EqualTo("Ben Ortiz"), "The one shuttle record is updated, not doubled.");
                Assert.That(comp.Mode, Is.EqualTo(modeBefore));
                Assert.That(comp.Locked, Is.EqualTo(lockedBefore));
                Assert.That(comp.AllowList, Has.Count.EqualTo(1), "Guests keep their access.");
                Assert.That(access.IsOwner(ship.Ada, grid), Is.False, "The old owner no longer manages the ship.");
                Assert.That(access.IsOwner(ship.Ben, grid), Is.True);
                Assert.That(result, Does.Contain("Ben Ortiz"));
            });

            // Back to Ada, keeping Ben aboard as crew and clearing the guests.
            server.PlayerMan.SetAttachedEntity(session, ship.Ada);
            var back = new WGShipAdminTransferMessage(netGrid, session.UserId) { KeepPrevious = true, ClearAllowList = true };
            Assert.That(ships.Transfer(session, grid, back, out result), Is.True, result);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<ShuttleDeedComponent>(ship.AdaCard), Is.True);
                Assert.That(entMan.HasComponent<ShuttleDeedComponent>(ship.BenCard), Is.False);
                Assert.That(comp.AllowList, Has.Count.EqualTo(1), "Only the previous owner's card is listed.");
                Assert.That(comp.AllowList[0].Name, Is.EqualTo("Ben Ortiz"));
            });

            // Handing it to the owner it already has leaves the deed on their card.
            Assert.That(ships.Transfer(session, grid, new WGShipAdminTransferMessage(netGrid, session.UserId), out result), Is.True, result);
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(ship.AdaCard), Is.True, "The owner's own card is free to take the deed back.");

            // A ghost has no card to hold a deed: the ship is registered to the account and unlocks.
            ghost = entMan.SpawnEntity(GhostProto, map.GridCoords);
            server.PlayerMan.SetAttachedEntity(session, ghost);
            access.SetLocked((grid, comp), true);
            Assert.That(ships.Transfer(session, grid, new WGShipAdminTransferMessage(netGrid, session.UserId), out result), Is.True, result);
            Assert.Multiple(() =>
            {
                Assert.That(shipyard.DeedHolders(grid), Is.Empty, "No card holds the deed.");
                Assert.That(comp.OwnerUsers, Is.EquivalentTo(new[] { session.UserId }));
                Assert.That(comp.Locked, Is.False, "A lock nobody's card can open is taken off.");
                Assert.That(access.IsOwner(ghost, grid), Is.True, "The account owns it in any body.");
            });

            Assert.That(ships.Transfer(session, grid, new WGShipAdminTransferMessage(netGrid, phantom), out result), Is.False, "An offline player is refused.");

            entMan.EnsureComponent<WGCompanyHangarShipComponent>(grid);
            Assert.That(ships.Transfer(session, grid, new WGShipAdminTransferMessage(netGrid, session.UserId), out result), Is.False, "A hangar ship is refused.");
            Assert.That(ships.Transfer(session, grid, new WGShipAdminTransferMessage(netGrid, session.UserId) { DetachHangar = true }, out result), Is.True, result);
            Assert.That(entMan.HasComponent<WGCompanyHangarShipComponent>(grid), Is.False);

            state = new WGShipAdminEuiState
            {
                Ships = ships.BuildList(WGShipAdminFilter.PlayerShips, out _),
                Selected = ships.BuildDetail(grid, null),
                ViewerFlags = ships.Flags(session),
            };
            Assert.That(state.Selected!.Owners, Has.Count.EqualTo(1));
        });

        await pair.RunTicksSync(5);
        await pair.Client.WaitAssertion(() =>
        {
            var window = new WGShipAdminWindow();
            window.OpenCentered();
            window.UpdateState(state);
            Assert.That(window.Ownership.OwnerCount, Is.EqualTo(1));
            window.Close();
        });

        await server.WaitPost(() =>
        {
            server.PlayerMan.SetAttachedEntity(session, null);
            entMan.DeleteEntity(ghost);
            ship.Delete(entMan);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OwnersAreAddedRemovedAndReleased()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WGShipAdminSystem>();
        var shipyard = entMan.System<ShipyardSystem>();
        var locks = entMan.System<ShuttleConsoleLockSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var phantom = new NetUserId(Guid.NewGuid());

        await server.WaitAssertion(() =>
        {
            var wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            var ship = SpawnOwnedShip(pair, map, session);
            var grid = ship.Grid;
            var comp = entMan.GetComponent<WGShipAccessComponent>(grid);

            Assert.That(ships.AddOwner(session, grid, phantom, true, out var result), Is.True, result);
            Assert.That(result, Does.Contain(phantom.ToString()), "An offline co-owner is named as not told.");
            Assert.That(ships.AddOwner(session, grid, phantom, false, out result), Is.False, "They can't be added twice.");
            Assert.That(comp.OwnerUsers, Does.Contain(phantom));

            Assert.That(ships.RemoveOwner(session, grid, phantom, null, false, out result), Is.True, result);
            Assert.That(comp.OwnerUsers, Does.Not.Contain(phantom));

            // Ada's deed card and the player's account are left; the account goes, then the card is the last owner.
            Assert.That(ships.RemoveOwner(session, grid, session.UserId, null, false, out result), Is.True, result);
            Assert.That(ships.RemoveOwner(session, grid, null, ship.AdaCard, false, out result), Is.False, "The last owner is not removed.");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(ship.AdaCard), Is.True);

            // The owner leaves the helm unlocked, as they would while flying.
            EntityUid helm = default;
            var consoles = entMan.EntityQueryEnumerator<ShuttleConsoleLockComponent, TransformComponent>();
            while (consoles.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    helm = uid;
            }

            Assert.That(helm.IsValid(), Is.True, "Precondition: the ship has a helm.");
            var helmLock = entMan.GetComponent<ShuttleConsoleLockComponent>(helm);
            Assert.That(locks.TryUnlock(helm, ship.AdaCard, helmLock), Is.True, "Precondition: the owner's card unlocks the helm.");
            Assert.That(locks.GetEffectiveLockState(helm, helmLock), Is.False);

            // It was bought with a voucher, which the helm lock matches against the ship's own deed.
            var shipDeed = entMan.GetComponent<ShuttleDeedComponent>(grid);
#pragma warning disable RA0002 // Stands in for a voucher purchase at a shipyard console.
            shipDeed.PurchasedWithVoucher = true;
            shipDeed.PurchaseVoucherUid = ship.GuestCard.ToString();
#pragma warning restore RA0002

            Assert.That(ships.Release(session, grid, false, out result), Is.True, result);
            Assert.Multiple(() =>
            {
                Assert.That(locks.GetEffectiveLockState(helm, helmLock), Is.True, "Nobody is left to lock the helm, so the release does.");
                Assert.That(shipDeed.PurchaseVoucherUid, Is.Null, "The voucher it was bought with no longer works the helm.");
                Assert.That(shipDeed.PurchasedWithVoucher, Is.True, "It is still a voucher ship.");
                Assert.That(shipyard.DeedHolders(grid), Is.Empty);
                Assert.That(entMan.HasComponent<ShipOwnershipComponent>(grid), Is.False);
                Assert.That(comp.OwnerUsers, Is.Empty);
                Assert.That(comp.Locked, Is.False, "A ship with no owner is unlocked.");
                Assert.That(ships.OwnerAccounts(grid), Is.Empty);
            });

            // A released ship can be handed out again.
            server.PlayerMan.SetAttachedEntity(session, ship.Ben);
            Assert.That(ships.Transfer(session, grid, new WGShipAdminTransferMessage(entMan.GetNetEntity(grid), session.UserId), out result), Is.True, result);
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(ship.BenCard), Is.True);
            Assert.That(entMan.GetComponent<ShuttleDeedComponent>(ship.BenCard).PurchasedWithVoucher, Is.True, "A shipyard still sees a voucher ship on the new owner's card.");

            server.PlayerMan.SetAttachedEntity(session, null);
            ship.Delete(entMan);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TransferSetsTheHullFactionAsAsked()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WGShipAdminSystem>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var stations = entMan.System<StationSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        await server.WaitAssertion(() =>
        {
            var wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            string? Faction(EntityUid hull)
            {
                return entMan.TryGetComponent<CompanyComponent>(hull, out var company) ? company.CompanyName.Id : null;
            }

            WGShipAdminTransferMessage Give(EntityUid hull, WGShipAdminFaction faction)
            {
                return new WGShipAdminTransferMessage(entMan.GetNetEntity(hull), session.UserId) { Faction = faction };
            }

            var ship = SpawnOwnedShip(pair, map, session);
            var grid = ship.Grid;
            entMan.EnsureComponent<CompanyComponent>(grid).CompanyName = HullFaction;
            foreach (var card in new[] { ship.AdaCard, ship.BenCard, ship.GuestCard })
                entMan.GetComponent<IdCardComponent>(card).CompanyName = CardFaction;

            // Ben's card already holds a deed, so it can't take this one. It still names his faction.
            entMan.AddComponent<ShuttleDeedComponent>(ship.BenCard);
            server.PlayerMan.SetAttachedEntity(session, ship.Ben);

            Assert.That(ships.Transfer(session, grid, Give(grid, WGShipAdminFaction.Keep), out var result), Is.True, result);
            Assert.That(Faction(grid), Is.EqualTo(HullFaction), "Keep leaves the hull's faction.");

            Assert.That(ships.Transfer(session, grid, Give(grid, WGShipAdminFaction.FromCard), out result), Is.True, result);
            Assert.That(Faction(grid), Is.EqualTo(CardFaction), "The faction is read from the ID he carries, though the deed could not go on it.");

            Assert.That(ships.Transfer(session, grid, Give(grid, WGShipAdminFaction.Clear), out result), Is.True, result);
            Assert.That(Faction(grid), Is.Null);

            // Ships nobody owned take the purchase path, which on its own puts the card's faction on the hull.
            var vessel = server.ProtoMan.Index<VesselPrototype>(VesselProto);
            var guestId = entMan.GetComponent<IdCardComponent>(ship.GuestCard);
            var hulls = new List<EntityUid>();
            server.PlayerMan.SetAttachedEntity(session, ship.Guest);
            foreach (var (option, onCard, expected) in new (WGShipAdminFaction, string, string?)[]
                     {
                         (WGShipAdminFaction.Keep, CardFaction, HullFaction),
                         (WGShipAdminFaction.Clear, CardFaction, null),
                         (WGShipAdminFaction.FromCard, CardFaction, CardFaction),
                         (WGShipAdminFaction.FromCard, NoFaction, NoFaction),
                     })
            {
                Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(160 + 80 * hulls.Count, 0), null, out var spawned), Is.True);
                var hull = spawned!.Value;
                hulls.Add(hull);
                Assert.That(entMan.HasComponent<WGShipAccessComponent>(hull) || entMan.HasComponent<ShuttleDeedComponent>(hull), Is.False, "Precondition: nobody owns it.");
                entMan.EnsureComponent<CompanyComponent>(hull).CompanyName = HullFaction;
                guestId.CompanyName = onCard;

                Assert.That(ships.Transfer(session, hull, Give(hull, option), out result), Is.True, result);
                Assert.That(entMan.HasComponent<ShuttleDeedComponent>(ship.GuestCard), Is.True, "Precondition: the purchase path ran.");
                Assert.That(Faction(hull), Is.EqualTo(expected), $"{option} on a new ship, with {onCard} on the card.");

                // Access is set up from the faction the hull ends with, not the one on the card.
                var mode = expected is null or NoFaction ? WGShipAccessMode.Private : WGShipAccessMode.Faction;
                Assert.That(entMan.GetComponent<WGShipAccessComponent>(hull).Mode, Is.EqualTo(mode), $"{option} on a new ship, with {onCard} on the card.");

                // The card is freed for the next ship.
                entMan.RemoveComponent<ShuttleDeedComponent>(ship.GuestCard);
            }

            server.PlayerMan.SetAttachedEntity(session, null);
            foreach (var hull in hulls)
            {
                var station = stations.GetOwningStation(hull);
                entMan.DeleteEntity(hull);
                if (station != null)
                    entMan.DeleteEntity(station.Value);
            }

            ship.Delete(entMan);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A Guppy bought by Ada, played by the test's player, with Ben and a guest standing by with cards of their own.</summary>
    internal static Ship SpawnOwnedShip(TestPair pair, TestMapData map, ICommonSession session)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var hands = entMan.System<SharedHandsSystem>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var shipyard = entMan.System<ShipyardSystem>();
        var access = entMan.System<WGShipAccessServerSystem>();

        // Deeds write a sector shuttle record; the host component gives it somewhere to go.
        var host = entMan.Spawn();
        entMan.EnsureComponent<StationSectorServiceHostComponent>(host);

        var vessel = server.ProtoMan.Index<VesselPrototype>(VesselProto);
        Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(80, 0), null, out var spawned, createStation: true), Is.True);
        var grid = spawned!.Value;

        var (ada, adaCard) = SpawnCrew(entMan, hands, map, "Ada Vance");
        var (ben, benCard) = SpawnCrew(entMan, hands, map, "Ben Ortiz");
        var (guest, guestCard) = SpawnCrew(entMan, hands, map, "Cy Reyes");

        server.PlayerMan.SetAttachedEntity(session, ada);
        Assert.That(shipyard.TryAssignDeed(grid, adaCard, session, vessel), Is.True);
        access.AddOwnerUser(grid, session.UserId);
        Assert.That(entMan.HasComponent<WGShipAccessComponent>(grid), Is.True, "Precondition: the purchase set ship access up.");

        return new Ship(grid, entMan.System<StationSystem>().GetOwningStation(grid), host, ada, adaCard, ben, benCard, guest, guestCard);
    }

    /// <summary>
    /// A person holding an ID card with a crew record of their own on the test map, as a spawned player has. One
    /// player plays every body here, and a card with no record would be given the player's own, the same for all.
    /// </summary>
    private static (EntityUid Person, EntityUid Card) SpawnCrew(IEntityManager entMan, SharedHandsSystem hands, TestMapData map, string name)
    {
        entMan.EnsureComponent<StationRecordsComponent>(map.MapUid);
        var (person, card) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, name, 0);
        var key = entMan.System<StationRecordsSystem>().AddRecordEntry(map.MapUid, new GeneralStationRecord { Name = name });
        entMan.System<StationRecordKeyStorageSystem>().AssignKey(card, key);
        return (person, card);
    }

    internal readonly record struct Ship(
        EntityUid Grid,
        EntityUid? Station,
        EntityUid Host,
        EntityUid Ada,
        EntityUid AdaCard,
        EntityUid Ben,
        EntityUid BenCard,
        EntityUid Guest,
        EntityUid GuestCard)
    {
        public void Delete(IEntityManager entMan)
        {
            foreach (var uid in new[] { AdaCard, BenCard, GuestCard, Ada, Ben, Guest, Grid, Host })
                entMan.DeleteEntity(uid);

            if (Station is { } station)
                entMan.DeleteEntity(station);
        }
    }
}
