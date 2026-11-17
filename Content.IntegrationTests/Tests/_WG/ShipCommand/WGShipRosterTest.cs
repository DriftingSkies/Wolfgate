#nullable enable
using System;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.ShipCommand;
using Content.Shared._WG.Traders;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>
/// The crew roster: posts staffed when the roster starts, with the roster's group and mission; a lost post stays empty
/// while automatic restaffing is off and is staffed by hand; switched on, it restaffs by itself within its hourly cap.
/// Counters are staffed with their trader facing the counter and restaffed when he goes; a post stood down or held
/// by a player relieves its crewman and is left alone until it is stood up or handed back.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipRosterSystem))]
public sealed class WGShipRosterTest
{
    [Test]
    public async Task PostsStaffAndRestaff()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var roster = entMan.System<WGShipRosterSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        Entity<WGShipRosterComponent> ship = new(EntityUid.Invalid, null!);
        await server.WaitPost(() =>
        {
            var a = entMan.SpawnEntity("WGShipCrewPostDeckhand", map.GridCoords);
            var b = entMan.SpawnEntity("WGShipCrewPostMarine", map.GridCoords);
            Assert.That(entMan.GetComponent<TransformComponent>(a).ParentUid, Is.EqualTo(grid), "Precondition: the post sits on the grid.");
            Assert.That(entMan.HasComponent<WGShipCrewPostComponent>(b), Is.True, "Precondition: the marker is a post.");
            ship = (grid, entMan.AddComponent<WGShipRosterComponent>(grid));
            // The test grid is not map-initialized, so do what map init (or a roster added to a running grid) does.
            roster.ReadPosts(ship);
            roster.Staff(ship, null, null);
        });

        EntityUid deckhand = default;
        await server.WaitAssertion(() =>
        {
            var slots = ship.Comp.Slots.Values.ToList();
            Assert.That(slots, Has.Count.EqualTo(2), "One slot per post.");
            Assert.That(slots.All(roster.Held), Is.True, "Both posts are staffed when the roster starts.");
            foreach (var slot in slots)
            {
                var mob = slot.Occupant!.Value;
                Assert.That(entMan.GetComponent<WGCrewComponent>(mob).Group, Is.EqualTo(ship.Comp.Group));
                Assert.That(entMan.HasComponent<WGCrewSecurityComponent>(mob), Is.True, "The roster's mission was applied.");
            }

            deckhand = slots.Single(s => s.Role.Id == "WGCrewDeckhand").Occupant!.Value;
            Assert.That(ship.Comp.AutoRestaff, Is.False, "Automatic restaffing is off by default.");
            entMan.DeleteEntity(deckhand);
        });

        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            var slot = ship.Comp.Slots.Values.Single(s => s.Role.Id == "WGCrewDeckhand");
            Assert.That(roster.Held(slot), Is.False, "With automatic restaffing off, a lost post stays empty.");
            Assert.That(roster.Staff(ship, null, null), Is.EqualTo(1), "Staffing by hand fills only the empty post.");
            Assert.That(roster.Held(slot), Is.True);

            // Automatic, with no delay and room for one restaff an hour: losing both posts brings back one crewman.
            ship.Comp.RestaffDelay = TimeSpan.Zero;
            ship.Comp.RestaffsPerHour = 1;
            Assert.That(roster.SetAutoRestaff(ship, true, null), Is.True);
            foreach (var s in ship.Comp.Slots.Values.ToList())
                entMan.DeleteEntity(s.Occupant!.Value);
        });

        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
        {
            Assert.That(ship.Comp.Slots.Values.Count(roster.Held), Is.EqualTo(1), "The hourly cap holds automatic restaffing to one.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CountersStandDownAndHandOver()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var roster = entMan.System<WGShipRosterSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        Entity<WGShipRosterComponent> ship = new(EntityUid.Invalid, null!);
        WGShipRosterSlot counter = default!, gunner = default!;
        EntityUid trader = default, crewman = default, player = default;
        await server.WaitPost(() =>
        {
            // The test grid is one tile; markers are anchored, so give them floor to stand on.
            var maps = entMan.System<SharedMapSystem>();
            for (var x = 1; x <= 3; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            var desk = entMan.SpawnEntity("WGShipCounterPostLupercalApothecary", map.GridCoords);
            entMan.System<SharedTransformSystem>().SetLocalRotation(desk, Angle.FromDegrees(90));
            entMan.SpawnEntity("WGShipCrewPostGunner", map.GridCoords.Offset(new System.Numerics.Vector2(1, 0)));
            entMan.SpawnEntity("WGShipCrewBay", map.GridCoords.Offset(new System.Numerics.Vector2(3, 0)));
            ship = (grid, entMan.AddComponent<WGShipRosterComponent>(grid));
            roster.ReadPosts(ship);
            roster.Staff(ship, null, null);
            counter = ship.Comp.Slots.Values.Single(s => s.Kind == WGShipRosterKind.Counter);
            gunner = ship.Comp.Slots.Values.Single(s => s.Kind == WGShipRosterKind.Crew);
            trader = counter.Occupant!.Value;
            crewman = gunner.Occupant!.Value;
            player = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(ship.Comp.Bay, Is.Not.Null, "The roster found the crew bay.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(trader).EntityPrototype?.ID, Is.EqualTo("WGLupercalApothecary"));
            Assert.That(entMan.HasComponent<TraderComponent>(trader), Is.True);
            Assert.That(entMan.GetComponent<TransformComponent>(trader).LocalRotation.Degrees, Is.EqualTo(90).Within(0.1),
                "The trader faces the way his post does, at the counter.");

            // A trader removed is replaced by hand like a crewman.
            entMan.DeleteEntity(trader);
            Assert.That(roster.Held(counter), Is.False);
            Assert.That(roster.Staff(ship, counter.Id, null), Is.EqualTo(1));
            Assert.That(roster.Held(counter), Is.True);

            // A post held by a player: the crewman is relieved and walks to the crew bay; nobody is sent while it's held.
            roster.Hold(ship, gunner, player);
            Assert.That(gunner.Occupant, Is.Null);
            Assert.That(entMan.HasComponent<WGShipRelievedComponent>(crewman), Is.True, "The relieved crewman is leaving.");
            Assert.That(entMan.GetComponent<WGCrewComponent>(crewman).Post, Is.EqualTo(ship.Comp.Bay), "He heads for the crew bay.");
            Assert.That(entMan.HasComponent<WGShipRosterMemberComponent>(crewman), Is.False, "He no longer answers for the post.");
            Assert.That(roster.Staff(ship, null, null), Is.EqualTo(0), "A held post is left to its player.");

            // Handed back: the roster sends a crewman again, who comes aboard at the bay and walks to the post.
            roster.Release(ship, gunner);
            Assert.That(gunner.Holder, Is.Null);
            Assert.That(roster.Staff(ship, null, null), Is.EqualTo(1));
            var newcomer = gunner.Occupant!.Value;
            Assert.That(entMan.GetComponent<WGCrewComponent>(newcomer).Post, Is.EqualTo(gunner.Post), "The newcomer's post is the gunner's.");

            // Stood down: relieved, and left empty until stood up again.
            Assert.That(roster.SetStoodDown(ship, gunner.Id, true, null), Is.True);
            Assert.That(entMan.HasComponent<WGShipRelievedComponent>(newcomer), Is.True);
            Assert.That(roster.Staff(ship, null, null), Is.EqualTo(0), "A post stood down is left empty.");
            Assert.That(roster.SetStoodDown(ship, gunner.Id, false, null), Is.True);
            Assert.That(roster.Staff(ship, null, null), Is.EqualTo(0), "Nobody is sent until the crewman stood down has left, or the buttons would spawn crew without limit.");
            entMan.DeleteEntity(newcomer);
            Assert.That(roster.Staff(ship, null, null), Is.EqualTo(1), "Once he has gone, it is staffed again.");
        });

        await pair.CleanReturnAsync();
    }
}
