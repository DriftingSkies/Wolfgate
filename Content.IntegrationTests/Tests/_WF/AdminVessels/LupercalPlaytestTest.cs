#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.GroundFire;
using Content.Server._WF.ShipCommand;
using Content.Server.Power.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.AdminVessels;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipCommand;
using Content.Shared.Damage.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Fax.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.AdminVessels;

/// <summary>The fixes from the Lupercal's first playtest (v0.29), on a freshly spawned ship.</summary>
[TestFixture]
public sealed class LupercalPlaytestTest
{
    [Test]
    public async Task SpawnsHardenedAndLockedDown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();
        EntityUid grid = default;

        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WFLupercal");
            Assert.That(server.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(120, 0), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            using var _ = Assert.EnterMultipleScope();

            // Locked down to visitors: the docks and the public side, while the staff side stays open to the staff list.
            var zones = em.GetComponent<WFShipZonesComponent>(grid).Zones;
            foreach (var zone in zones)
            {
                var expected = zone.Id.EndsWith("-staff") || zone.Id == "lockers" ? WFShipZoneMode.Staff : WFShipZoneMode.Locked;
                Assert.That(zone.Mode, Is.EqualTo(expected), $"Zone {zone.Id}.");
            }

            // No door aboard pries, and the staff side's edges are hard-light barriers.
            var doors = OnGrid<DoorComponent>(em, grid);
            Assert.That(doors.Where(d => d.Comp.CanPry).Select(d => Proto(em, d.Owner)).Distinct(), Is.Empty, "Every door is unpryable.");
            Assert.That(OfProto(em, grid, "WFHoloDoor"), Is.Not.Empty, "The staff zones are bounded by hard-light barriers.");

            // Every ship gun, hardpoint and sentry is indestructible, and the guns carry four times the charge.
            var guns = OnGrid<ShipGunTypeComponent>(em, grid);
            Assert.That(guns, Is.Not.Empty);
            foreach (var gun in guns)
                Assert.That(em.HasComponent<GodmodeComponent>(gun), Is.True, $"{Proto(em, gun)} is indestructible.");
            foreach (var hardpoint in OnGrid<HardpointComponent>(em, grid))
                Assert.That(em.HasComponent<GodmodeComponent>(hardpoint), Is.True, "Hardpoints are indestructible.");
            var sentries = OfProto(em, grid, "WFLupercalSentry");
            Assert.That(sentries, Has.Count.EqualTo(8), "Two sentries in each core gate.");
            Assert.That(sentries.All(em.HasComponent<GodmodeComponent>), Is.True, "The sentries are indestructible.");
            var cerberus = OfProto(em, grid, "WeaponTurretCerberus").First();
            Assert.That(em.GetComponent<BatteryComponent>(cerberus).MaxCharge, Is.EqualTo(160000f), "Four times the Cerberus's charge.");

            // The shield powers itself; the cranes run at top speed.
            var shield = OfProto(em, grid, "WFLupercalShieldGenerator").Single();
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(shield).NeedsPower, Is.False, "The shield needs no grid power.");
            foreach (var crane in OnGrid<WFDrydockCraneComponent>(em, grid))
                Assert.That(crane.Comp.SpeedMultiplier, Is.EqualTo(3f), "The berth cranes run at the top tier's speed.");

            // A warp point, no fire spread, no strewn litter, faxes with their own names.
            Assert.That(OfProto(em, grid, "WarpPoint"), Has.Count.EqualTo(1), "One warp point.");
            Assert.That(em.HasComponent<WFNoFireSpreadComponent>(grid), Is.True, "Fires don't spread aboard.");
            var station = server.System<StationSystem>().GetOwningStation(grid);
            Assert.That(station, Is.Not.Null);
            Assert.That(em.HasComponent<StationVariationHasRunComponent>(station!.Value), Is.True, "No round-start litter.");
            var faxNames = OnGrid<FaxMachineComponent>(em, grid).Select(f => f.Comp.FaxName).ToList();
            Assert.That(faxNames, Has.Count.GreaterThan(1));
            Assert.That(faxNames, Is.Unique, "Every fax has its own address.");
            Assert.That(faxNames, Has.None.EqualTo("Unknown"));

            // The roster: elite, never dry, replaced when lost, repairing on its own, two janitors.
            var roster = em.GetComponent<WFShipRosterComponent>(grid);
            Assert.That(roster.AutoRestaff && roster.AutoRepair && roster.InfiniteAmmo, Is.True);
            Assert.That(roster.Skill, Is.EqualTo(WFCrewSkill.Elite));
            Assert.That(roster.Slots.Values.Count(s => s.Role.Id == "WFCrewJanitor"), Is.EqualTo(2), "Two janitor posts.");
        });

        // A core gate's doors: with one open, the other won't open. A fresh ship's grid power hasn't come up yet, and an
        // unpowered airlock never opens, so both doors are powered here; only the interlock is left to stop them.
        await server.WaitAssertion(() =>
        {
            var doorSystem = server.System<SharedDoorSystem>();
            var gate = OfProto(em, grid, "AirlockCommandGlass").First(em.HasComponent<WFDoorInterlockComponent>);
            var partner = em.GetComponent<WFDoorInterlockComponent>(gate).Partner!.Value;
            foreach (var uid in new[] { gate, partner })
            {
                var powered = new PowerChangedEvent(true, 0f);
                em.EventBus.RaiseLocalEvent(uid, ref powered);
            }

            var door = em.GetComponent<DoorComponent>(gate);
            var partnerDoor = em.GetComponent<DoorComponent>(partner);
            Assert.That(em.GetComponent<AirlockComponent>(partner).Powered, Is.True, "Precondition: the partner has power.");
            Assert.That(doorSystem.CanOpen(partner, partnerDoor), Is.True, "Precondition: with both shut, the partner opens.");
            doorSystem.StartOpening(gate, door);
            Assert.That(door.State, Is.Not.EqualTo(DoorState.Closed), "Precondition: the first gate door is opening.");
            Assert.That(doorSystem.CanOpen(partner, partnerDoor), Is.False, "Its partner stays shut while it is open.");
            doorSystem.SetState(gate, DoorState.Closed, door);
            Assert.That(doorSystem.CanOpen(partner, partnerDoor), Is.True, "Once it closes, the partner opens again.");
        });

        // A dead crewman no longer bumps doors open.
        await server.WaitAssertion(() =>
        {
            var crewman = em.GetComponent<WFShipRosterComponent>(grid).Slots.Values
                .First(s => s.Role.Id == "WFCrewMarine" && s.Occupant != null).Occupant!.Value;
            var tags = server.System<TagSystem>();
            Assert.That(tags.HasTag(crewman, "DoorBumpOpener"), Is.True, "Precondition: a living crewman bumps doors open.");
            server.System<MobStateSystem>().ChangeMobState(crewman, MobState.Dead);
            Assert.That(tags.HasTag(crewman, "DoorBumpOpener"), Is.False, "A dead one doesn't.");
        });

        await pair.CleanReturnAsync();
    }

    private static List<Entity<T>> OnGrid<T>(IEntityManager em, EntityUid grid) where T : IComponent
    {
        var found = new List<Entity<T>>();
        var query = em.EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid == grid)
                found.Add((uid, comp));
        }

        return found;
    }

    private static List<EntityUid> OfProto(IEntityManager em, EntityUid grid, string proto)
    {
        var found = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.GridUid == grid && meta.EntityPrototype?.ID == proto)
                found.Add(uid);
        }

        return found;
    }

    private static string? Proto(IEntityManager em, EntityUid uid) => em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
}
