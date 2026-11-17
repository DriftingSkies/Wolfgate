#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server.Administration.Managers;
using Content.Server._WG.ShipCommand;
using Content.Server.Storage.EntitySystems;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>
/// The recovery pad: admins only by default; it lists the dead and the hurt first; it brings back a dead player from
/// another grid on the map, out of every container holding them, and it won't recall someone alive and awake until the
/// operator confirms, then counts them down before it does.
/// </summary>
[TestFixture]
[TestOf(typeof(WGRecoveryPadSystem))]
public sealed class WGRecoveryPadTest
{
    /// <summary>A character for the pad to find: a body with a mind, as a player's (or one who left) has.</summary>
    private static EntityUid Person(IEntityManager entMan, EntityCoordinates where, string name)
    {
        var mindSys = entMan.System<SharedMindSystem>();
        var body = entMan.SpawnEntity("MobHuman", where);
        var mind = mindSys.CreateMind(null, name);
        mindSys.TransferTo(mind, body, mind: mind.Comp);
        return body;
    }

    [Test]
    public async Task AdminOnlyByDefault()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var admins = server.ResolveDependency<IAdminManager>();
        var pads = entMan.System<WGRecoveryPadSystem>();
        var session = pair.Player!;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var padUid = entMan.SpawnEntity("WGRecoveryPad", map.GridCoords);
            var pad = new Entity<WGRecoveryPadComponent>(padUid, entMan.GetComponent<WGRecoveryPadComponent>(padUid));
            var target = Person(entMan, map.GridCoords, "Somebody");
            entMan.System<MobStateSystem>().ChangeMobState(target, MobState.Dead);

            var body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, body);
            if (admins.IsAdmin(session))
                admins.DeAdmin(session);

            Assert.Multiple(() =>
            {
                Assert.That(pad.Comp.Access, Is.EqualTo(WGShipAuthority.Admin), "The pad is for admins by default.");
                Assert.That(pads.CanUse(body, pad), Is.False, "A plain player may not use it.");
                Assert.That(pads.TryRecall(pad, body, target, true), Is.EqualTo(WGRecoveryPadRefusal.NotAllowed));
                Assert.That(pads.SetAccess(pad, body, WGShipAuthority.Staff), Is.False, "Nor open it up.");
            });

            if (admins.IsAdmin(session, includeDeAdmin: true))
                admins.ReAdmin(session);
            else
                admins.PromoteHost(session);

            Assert.Multiple(() =>
            {
                Assert.That(pads.CanUse(body, pad), Is.True, "An admin may.");
                Assert.That(pads.CanUse(target, pad), Is.False, "Someone with no standing still may not.");
                Assert.That(pads.SetAccess(pad, body, WGShipAuthority.Barred), Is.False, "The pad is never opened to everyone.");
            });

            entMan.DeleteEntity(target);
            entMan.DeleteEntity(padUid);
        });

        await server.WaitPost(() => admins.DeAdmin(session));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RecallsTheDeadAndWarnsTheLiving()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var admins = server.ResolveDependency<IAdminManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var pads = entMan.System<WGRecoveryPadSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var session = pair.Player!;
        var map = await pair.CreateTestMap();

        Entity<WGRecoveryPadComponent> pad = new(EntityUid.Invalid, null!);
        EntityUid op = default, dead = default, living = default;
        await server.WaitPost(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            var away = maps.CreateGridEntity(map.MapId);
            maps.SetTile(away, away.Comp, Vector2i.Zero, map.Tile.Tile);
            maps.SetTile(away, away.Comp, Vector2i.Right, map.Tile.Tile);
            xforms.SetWorldPosition(away, new Vector2(60, 60));

            var padUid = entMan.SpawnEntity("WGRecoveryPad", map.GridCoords);
            pad = (padUid, entMan.GetComponent<WGRecoveryPadComponent>(padUid));
            pad.Comp.ChargeTime = TimeSpan.FromSeconds(2);

            op = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, op);
            if (admins.IsAdmin(session, includeDeAdmin: true))
                admins.ReAdmin(session);
            else
                admins.PromoteHost(session);

            dead = Person(entMan, new EntityCoordinates(away, 0.5f, 0.5f), "Dead One");
            entMan.System<MobStateSystem>().ChangeMobState(dead, MobState.Dead);
            living = Person(entMan, new EntityCoordinates(away, 1.5f, 0.5f), "Live One");
            var blunt = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 20);
            entMan.System<DamageableSystem>().TryChangeDamage(living, blunt, ignoreResistances: true);
        });

        await server.WaitAssertion(() =>
        {
            var listed = pads.People(pad).Select(p => entMan.GetEntity(p.Entity)).ToList();
            Assert.That(listed, Does.Contain(dead).And.Contain(living).And.Contain(op), "The pad lists everyone with a mind on its map.");
            Assert.Multiple(() =>
            {
                Assert.That(listed.IndexOf(dead), Is.LessThan(listed.IndexOf(living)), "The dead come first.");
                Assert.That(listed.IndexOf(living), Is.LessThan(listed.IndexOf(op)),
                    "The hurt come before the unhurt, however far away they are.");
            });
            Assert.That(pads.TryRecall(pad, op, dead, confirmed: false), Is.EqualTo(WGRecoveryPadRefusal.None),
                "The dead need no confirmation.");
            Assert.That(pads.TryRecall(pad, op, living, confirmed: true), Is.EqualTo(WGRecoveryPadRefusal.Busy),
                "One recall at a time.");
        });

        await pair.RunTicksSync(180);
        await server.WaitAssertion(() =>
        {
            var padAt = xforms.GetMapCoordinates(pad);
            Assert.Multiple(() =>
            {
                Assert.That((xforms.GetMapCoordinates(dead).Position - padAt.Position).Length(), Is.LessThan(0.1f),
                    "The dead player is on the pad.");
                Assert.That(pad.Comp.Target, Is.Null);
                Assert.That(pad.Comp.ReadyAt, Is.GreaterThan(timing.CurTime), "And the pad is cooling down.");
            });

            pad.Comp.ReadyAt = TimeSpan.Zero;
            Assert.That(pads.TryRecall(pad, op, living, confirmed: false), Is.EqualTo(WGRecoveryPadRefusal.NeedsConfirmation),
                "Someone alive and awake needs the operator's confirmation.");
            Assert.That(pad.Comp.Target, Is.Null, "Nothing happens without it.");
            Assert.That(pads.TryRecall(pad, op, living, confirmed: true), Is.EqualTo(WGRecoveryPadRefusal.None));
            Assert.That(pad.Comp.Warn, Is.True, "The living get a countdown.");
        });

        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(pad.Comp.Warned, Is.InRange(1, 2), "The countdown has started.");
        });

        await pair.RunTicksSync(180);
        await server.WaitAssertion(() =>
        {
            var padAt = xforms.GetMapCoordinates(pad);
            Assert.That((xforms.GetMapCoordinates(living).Position - padAt.Position).Length(), Is.LessThan(0.1f),
                "After the countdown the living player is on the pad too.");
        });

        await server.WaitPost(() => admins.DeAdmin(session));
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body in a crate held in another container comes out of both onto the pad: not dropped into the outer one, and not moved
    /// while still flagged as inside a container.
    /// </summary>
    [Test]
    public async Task RecallsOutOfNestedContainers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var admins = server.ResolveDependency<IAdminManager>();
        var pads = entMan.System<WGRecoveryPadSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var storage = entMan.System<EntityStorageSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var session = pair.Player!;
        var map = await pair.CreateTestMap();

        Entity<WGRecoveryPadComponent> pad = new(EntityUid.Invalid, null!);
        EntityUid op = default, dead = default, bag = default, morgue = default;
        await server.WaitPost(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            var away = maps.CreateGridEntity(map.MapId);
            maps.SetTile(away, away.Comp, Vector2i.Zero, map.Tile.Tile);
            maps.SetTile(away, away.Comp, Vector2i.Right, map.Tile.Tile);
            xforms.SetWorldPosition(away, new Vector2(60, 60));

            var padUid = entMan.SpawnEntity("WGRecoveryPad", map.GridCoords);
            pad = (padUid, entMan.GetComponent<WGRecoveryPadComponent>(padUid));
            pad.Comp.ChargeTime = TimeSpan.FromSeconds(1);

            op = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, op);
            if (admins.IsAdmin(session, includeDeAdmin: true))
                admins.ReAdmin(session);
            else
                admins.PromoteHost(session);

            // A body in a crate, the crate held in a container of another entity. (An unfolded body bag can't go in a
            // container at all, FoldableSystem, so the audit's bag in a morgue tray doesn't arise.)
            morgue = entMan.SpawnEntity("Morgue", new EntityCoordinates(away, 0.5f, 0.5f));
            bag = entMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(away, 1.5f, 0.5f));
            dead = Person(entMan, new EntityCoordinates(away, 1.5f, 0.5f), "Crated One");
            entMan.System<MobStateSystem>().ChangeMobState(dead, MobState.Dead);
            // Closed first: an open storage drops what it's given beside itself instead of taking it in.
            if (storage.IsOpen(bag))
                storage.CloseStorage(bag);
            Assert.That(storage.Insert(dead, bag), Is.True, "The body goes in the crate.");
            var hold = containers.EnsureContainer<Container>(morgue, "wg_test_hold");
            Assert.That(containers.Insert(bag, hold), Is.True, "The crate goes in the hold.");
        });

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(containers.IsEntityInContainer(dead), Is.True, "Precondition: the body is in the crate.");
                Assert.That(containers.IsEntityInContainer(bag), Is.True, "Precondition: the crate is in the hold.");
            });
            Assert.That(pads.TryRecall(pad, op, dead, confirmed: false), Is.EqualTo(WGRecoveryPadRefusal.None));
        });

        await pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            var padAt = xforms.GetMapCoordinates(pad);
            Assert.Multiple(() =>
            {
                Assert.That(pad.Comp.Target, Is.Null, "The recall finished.");
                Assert.That(containers.IsEntityInContainer(dead), Is.False, "The body is still flagged as inside a container.");
                Assert.That(containers.IsEntityOrParentInContainer(dead), Is.False, "The body is still inside something.");
                Assert.That((xforms.GetMapCoordinates(dead).Position - padAt.Position).Length(), Is.LessThan(0.1f),
                    "The body isn't on the pad.");
                Assert.That(containers.IsEntityInContainer(bag), Is.True, "The crate left the hold with the body.");
            });

            entMan.DeleteEntity(dead);
            entMan.DeleteEntity(morgue);
        });

        await server.WaitPost(() => admins.DeAdmin(session));
        await pair.CleanReturnAsync();
    }
}
