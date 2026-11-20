#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Tugs;
using Content.Shared._WG.Tugs;
using Content.Shared._WG.Vehicles.Base;
using Content.Shared._WG.Vehicles.Grid.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.Damage;
using Content.Shared.Stunnable;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Localization;
using Robust.Shared.Timing;
using System.Linq;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._WG.Tugs;

/// <summary>A rider drives a luggage tug that tows trolleys along its path, and crates strap onto the trolleys.</summary>
[TestFixture]
public sealed class LuggageTugTest
{
    private const float StartX = 4.5f;
    private const float StartY = 20.5f;

    private sealed class Rig
    {
        public required TestMapData Map;
        public EntityUid Tug;
        public EntityUid Driver;
        public readonly List<EntityUid> Trolleys = new();
    }

    /// <summary>An open plating floor with the tug facing south and six trolleys lined up north of it.</summary>
    private static async Task<Rig> Build(TestPair pair)
    {
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var rig = new Rig { Map = map };

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;

            for (var x = -10; x < 20; x++)
            {
                for (var y = -15; y < 30; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));
                }
            }

            rig.Tug = entMan.SpawnEntity("WGLuggageTug", new EntityCoordinates(map.Grid.Owner, StartX, StartY));
            rig.Driver = entMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, StartX, StartY));
            // The test map has no air; the rider's health is about the tug, not the vacuum.
            entMan.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(rig.Driver);
            entMan.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(rig.Driver);

            for (var i = 1; i <= 6; i++)
            {
                rig.Trolleys.Add(entMan.SpawnEntity("WGLuggageTrolley", new EntityCoordinates(map.Grid.Owner, StartX, StartY + i)));
            }
        });
        await server.WaitRunTicks(5);

        return rig;
    }

    private static Entity<WGTugComponent> Tug(TestPair pair, Rig rig)
    {
        return (rig.Tug, pair.Server.EntMan.GetComponent<WGTugComponent>(rig.Tug));
    }

    private static Entity<WGTugTrolleyComponent> Trolley(TestPair pair, EntityUid uid)
    {
        return (uid, pair.Server.EntMan.GetComponent<WGTugTrolleyComponent>(uid));
    }

    private static Vector2 Local(TestPair pair, EntityUid uid)
    {
        return pair.Server.EntMan.GetComponent<TransformComponent>(uid).LocalPosition;
    }

    /// <summary>Buckling in makes the rider the operator; unbuckling clears it.</summary>
    [Test]
    public async Task BucklingInTakesTheWheel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var buckle = server.System<SharedBuckleSystem>();
            Assert.That(buckle.TryBuckle(rig.Driver, rig.Driver, rig.Tug), Is.True, "The rider could not buckle in.");
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BuckleComponent>(rig.Driver).BuckledTo, Is.EqualTo(rig.Tug), "The rider fell off.");
            Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Tug).Operator, Is.EqualTo(rig.Driver));
            Assert.That(entMan.HasComponent<GridVehicleOperatorComponent>(rig.Driver), Is.True);
        });

        // Buckling has a short cooldown before you can get up.
        await server.WaitRunTicks(90);
        await server.WaitAssertion(() =>
        {
            var buckle = server.System<SharedBuckleSystem>();
            Assert.That(buckle.TryUnbuckle(rig.Driver, rig.Driver), Is.True, "The rider could not unbuckle.");
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<RMCVehicleComponent>(rig.Tug).Operator, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Five trolleys couple in a row, a sixth is refused, and each one slows the tug.</summary>
    [Test]
    public async Task CouplesUpToFiveTrolleys()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var tugs = server.System<WGTugSystem>();
            var mover = entMan.GetComponent<GridVehicleMoverComponent>(rig.Tug);
            var baseSpeed = mover.MaxSpeed;

            for (var i = 0; i < 5; i++)
            {
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, rig.Trolleys[i])), Is.True, $"Trolley {i + 1} did not couple.");
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, rig.Trolleys[5])), Is.False, "A sixth trolley coupled.");
                Assert.That(Tug(pair, rig).Comp.Trolleys, Has.Count.EqualTo(5));
                Assert.That(mover.MaxSpeed, Is.LessThan(baseSpeed), "Towing did not slow the tug.");
            }

            // Uncoupling the third car drops it and the two behind.
            Assert.That(tugs.TryUncouple(Trolley(pair, rig.Trolleys[2])), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(Tug(pair, rig).Comp.Trolleys, Is.EqualTo(new[] { rig.Trolleys[0], rig.Trolleys[1] }));
                Assert.That(Trolley(pair, rig.Trolleys[4]).Comp.Tug, Is.Null);

                var released = entMan.GetComponent<PhysicsComponent>(rig.Trolleys[2]);
                Assert.That(released.BodyType, Is.EqualTo(BodyType.Dynamic), "A released trolley stayed kinematic.");
                Assert.That(released.CanCollide, Is.True, "A released trolley stayed non-colliding.");
                Assert.That(Trolley(pair, rig.Trolleys[2]).Comp.Towed, Is.False);

                var towed = entMan.GetComponent<PhysicsComponent>(rig.Trolleys[1]);
                Assert.That(towed.BodyType, Is.EqualTo(BodyType.Kinematic), "A towed trolley is not kinematic.");
                Assert.That(towed.CanCollide, Is.False, "A towed trolley still collides.");
                Assert.That(Trolley(pair, rig.Trolleys[1]).Comp.Towed, Is.True);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The rider is still in the seat, unhurt, standing, and not counted as run over by their own tug.</summary>
    private static void AssertRiderUnharmed(TestPair pair, Rig rig)
    {
        var entMan = pair.Server.EntMan;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entMan.GetComponent<BuckleComponent>(rig.Driver).BuckledTo, Is.EqualTo(rig.Tug), "The rider fell off while driving.");
            Assert.That(entMan.GetComponent<DamageableComponent>(rig.Driver).TotalDamage.Value, Is.Zero, "The tug hurt its own rider.");
            Assert.That(entMan.HasComponent<VehicleRunoverComponent>(rig.Driver), Is.False, "The tug ran over its own rider.");
            Assert.That(entMan.HasComponent<KnockedDownComponent>(rig.Driver), Is.False, "The rider was knocked down.");
            var seat = entMan.GetComponent<StrapComponent>(rig.Tug).BuckleOffset;
            Assert.That((Local(pair, rig.Driver) - seat).Length(), Is.LessThan(0.05f), "The rider was shoved off the seat.");
        }
    }

    private static async Task Ride(TestPair pair, Rig rig)
    {
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(rig.Driver, rig.Driver, rig.Tug), Is.True);
        });
        await server.WaitRunTicks(2);
    }

    /// <summary>Driving through a loose crate and over a bystander, the rider is neither hurt nor shoved off the seat.</summary>
    [Test]
    public async Task TheTugNeverHurtsItsOwnRider()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("CrateGeneric", new EntityCoordinates(rig.Map.Grid.Owner, StartX, StartY - 3f));
            entMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Map.Grid.Owner, StartX, StartY - 6f));
        });
        await Ride(pair, rig);

        await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = MoveButtons.Up);
        await server.WaitRunTicks(240);

        await server.WaitAssertion(() =>
        {
            Assert.That(Local(pair, rig.Tug).Y, Is.LessThan(StartY - 6f), "The tug was stopped on its way.");
            AssertRiderUnharmed(pair, rig);
        });

        // Getting off puts the rider on free floor beside the tug, not inside its body.
        await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = MoveButtons.None);
        await server.WaitRunTicks(120);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedBuckleSystem>().TryUnbuckle(rig.Driver, rig.Driver), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var gap = (Local(pair, rig.Driver) - Local(pair, rig.Tug)).Length();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.GetComponent<TransformComponent>(rig.Driver).ParentUid, Is.EqualTo(rig.Map.Grid.Owner));
                Assert.That(gap, Is.GreaterThan(0.6f).And.LessThan(2f), "The rider did not get off onto the floor beside the tug.");
                Assert.That(entMan.HasComponent<KnockedDownComponent>(rig.Driver), Is.False, "Dismounting knocked the rider down.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Backing up past the start of the trail keeps the trolley behind the tug, and a wall behind the train stops it.</summary>
    [Test]
    public async Task ReversingKeepsTheTrainBehind()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            // A wall across the grid five tiles behind the tug.
            for (var x = -10; x < 20; x++)
                entMan.SpawnEntity("WallSolid", new EntityCoordinates(rig.Map.Grid.Owner, x + 0.5f, StartY + 5f));
        });
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<WGTugSystem>().TryCouple(Tug(pair, rig), Trolley(pair, rig.Trolleys[0])), Is.True);
        });
        await Ride(pair, rig);

        // The tug faces south, so Down reverses it up the grid, past the one point its trail holds.
        await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = MoveButtons.Down);
        await server.WaitRunTicks(240);

        await server.WaitAssertion(() =>
        {
            var tug = Local(pair, rig.Tug);
            var car = Local(pair, rig.Trolleys[0]);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(tug.Y, Is.GreaterThan(StartY + 1.5f), "The tug did not back up.");
                Assert.That(car.Y - tug.Y, Is.EqualTo(Tug(pair, rig).Comp.HitchDistance).Within(0.15f), "Reversing moved the trolley off its spacing behind the tug.");
                Assert.That(car.X, Is.EqualTo(tug.X).Within(0.2f));
                Assert.That(car.Y, Is.LessThan(StartY + 4.6f), "The trolley was pushed into the wall.");
                Assert.That(Tug(pair, rig).Comp.Trolleys, Has.Count.EqualTo(1), "The train came apart while reversing.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A trolley beside the tug, or across its line, cannot be coupled.</summary>
    [Test]
    public async Task OnlyATrolleyBehindTheTugCouples()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var tugs = server.System<WGTugSystem>();
            var beside = entMan.SpawnEntity("WGLuggageTrolley", new EntityCoordinates(rig.Map.Grid.Owner, StartX + 1f, StartY));
            var inFront = entMan.SpawnEntity("WGLuggageTrolley", new EntityCoordinates(rig.Map.Grid.Owner, StartX, StartY - 1f));
            var askew = entMan.SpawnEntity("WGLuggageTrolley", new EntityCoordinates(rig.Map.Grid.Owner, StartX + 0.8f, StartY + 1f));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, beside)), Is.False, "A trolley beside the tug coupled.");
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, inFront)), Is.False, "A trolley in front of the tug coupled.");
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, askew)), Is.False, "A trolley off the tug's line coupled.");
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, rig.Trolleys[0])), Is.True, "The trolley right behind did not couple.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Driving pulls the train along the tug's path at its spacing, and leaving the grid drops it.</summary>
    [Test]
    public async Task TrainFollowsTheTug()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var tugs = server.System<WGTugSystem>();
            for (var i = 0; i < 3; i++)
            {
                Assert.That(tugs.TryCouple(Tug(pair, rig), Trolley(pair, rig.Trolleys[i])), Is.True);
            }

            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(rig.Driver, rig.Driver, rig.Tug), Is.True);
        });
        await server.WaitRunTicks(2);

        // The tug faces south, so forward drives it down the grid.
        await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = MoveButtons.Up);
        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var tug = Local(pair, rig.Tug);
            Assert.That(tug.Y, Is.LessThan(StartY - 3f), "The tug barely moved.");
            AssertRiderUnharmed(pair, rig);

            for (var i = 0; i < 3; i++)
            {
                var car = Local(pair, rig.Trolleys[i]);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(car.X, Is.EqualTo(tug.X).Within(0.2f), $"Trolley {i + 1} left the path.");
                    Assert.That(car.Y - tug.Y, Is.EqualTo(WGTugTrail.Distance(Tug(pair, rig).Comp, i)).Within(0.15f), $"Trolley {i + 1} is not at its spacing.");
                }
            }
        });

        await server.WaitPost(() =>
        {
            entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = MoveButtons.None;
            var xform = entMan.System<SharedTransformSystem>();
            xform.SetCoordinates(rig.Tug, new EntityCoordinates(rig.Map.MapUid, new Vector2(100f, 100f)));
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(Tug(pair, rig).Comp.Trolleys, Is.Empty, "The train stayed coupled to a tug that left its grid.");
            Assert.That(Trolley(pair, rig.Trolleys[0]).Comp.Tug, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    private static Verb? FindVerb(TestPair pair, EntityUid target, EntityUid user, string key)
    {
        var server = pair.Server;
        var text = server.ResolveDependency<ILocalizationManager>().GetString(key);
        return server.System<SharedVerbSystem>().GetLocalVerbs(target, user, typeof(AlternativeVerb), force: true).FirstOrDefault(v => v.Text == text);
    }

    private static int Ticks(TestPair pair, double seconds) => (int) Math.Ceiling(seconds * pair.Server.Timing.TickRate);

    /// <summary>Strapping a crate on and off goes through the verbs and their do-afters: not from too far, and not while the train moves.</summary>
    [Test]
    public async Task CargoVerbsNeedReachAndAStoppedTrain()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var trolleyUid = rig.Trolleys[0];
        EntityUid user = default, crate = default;

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<WGTugSystem>().TryCouple(Tug(pair, rig), Trolley(pair, trolleyUid)), Is.True);
            user = entMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Map.Grid.Owner, StartX + 1f, StartY + 1f));
            crate = entMan.SpawnEntity("CrateGeneric", new EntityCoordinates(rig.Map.Grid.Owner, StartX + 1f, StartY + 2f));
            entMan.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(user);
            entMan.RemoveComponent<Content.Server.Body.Components.RespiratorComponent>(user);
        });
        await server.WaitRunTicks(2);

        // Too far away, no verb; pulling the crate next to the trolley, the verb is offered.
        await server.WaitAssertion(() =>
        {
            server.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(rig.Map.Grid.Owner, StartX + 4f, StartY + 1f));
            server.System<SharedTransformSystem>().SetCoordinates(crate, new EntityCoordinates(rig.Map.Grid.Owner, StartX + 4f, StartY + 2f));
            Assert.That(server.System<PullingSystem>().TryStartPull(user, crate), Is.True, "Test setup: the user could not pull the crate.");
            Assert.That(FindVerb(pair, trolleyUid, user, "wg-trolley-verb-load"), Is.Null, "A crate was offered for strapping from too far away.");

            server.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(rig.Map.Grid.Owner, StartX + 1f, StartY + 1f));
            server.System<SharedTransformSystem>().SetCoordinates(crate, new EntityCoordinates(rig.Map.Grid.Owner, StartX + 1f, StartY + 2f));
            if (!entMan.GetComponent<PullerComponent>(user).Pulling.HasValue)
                Assert.That(server.System<PullingSystem>().TryStartPull(user, crate), Is.True, "Test setup: the user could not pull the crate again.");
            var load = FindVerb(pair, trolleyUid, user, "wg-trolley-verb-load");
            Assert.That(load, Is.Not.Null, "The pulled crate was not offered for strapping.");
            server.System<SharedVerbSystem>().ExecuteVerb(load!, user, trolleyUid, forced: true);
        });

        // The train sets off before the do-after ends: the crate stays loose.
        await Ride(pair, rig);
        await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = MoveButtons.Up);
        await server.WaitRunTicks(Ticks(pair, 2.5));
        // Without a parking brake the tug coasts a good while before it rests.
        await server.WaitPost(() => entMan.GetComponent<InputMoverComponent>(rig.Driver).HeldMoveButtons = MoveButtons.None);
        await server.WaitRunTicks(Ticks(pair, 8));

        await server.WaitAssertion(() =>
        {
            Assert.That(Trolley(pair, trolleyUid).Comp.Cargo!.ContainedEntity, Is.Null, "A crate was strapped onto a moving train.");
            var mover = entMan.GetComponent<GridVehicleMoverComponent>(rig.Tug);
            Assert.That(mover.IsMoving || MathF.Abs(mover.CurrentSpeed) > 0.05f, Is.False, "Test setup: the tug is still rolling.");

            // Stopped again: bring the crate alongside and strap it on through the do-after.
            var trolleyPos = Local(pair, trolleyUid);
            server.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(rig.Map.Grid.Owner, trolleyPos + new Vector2(1f, 0f)));
            server.System<SharedTransformSystem>().SetCoordinates(crate, new EntityCoordinates(rig.Map.Grid.Owner, trolleyPos + new Vector2(1f, 1f)));
            if (!entMan.GetComponent<PullerComponent>(user).Pulling.HasValue)
                Assert.That(server.System<PullingSystem>().TryStartPull(user, crate), Is.True, "Test setup: the user could not pull the crate again.");
            var load = FindVerb(pair, trolleyUid, user, "wg-trolley-verb-load");
            Assert.That(load, Is.Not.Null, "The crate was not offered for strapping once the train stopped.");
            server.System<SharedVerbSystem>().ExecuteVerb(load!, user, trolleyUid, forced: true);
        });
        await server.WaitRunTicks(Ticks(pair, 2.5));

        await server.WaitAssertion(() =>
        {
            Assert.That(Trolley(pair, trolleyUid).Comp.Cargo!.ContainedEntity, Is.EqualTo(crate), "The do-after did not strap the crate on.");
            var unload = FindVerb(pair, trolleyUid, user, "wg-trolley-verb-unload");
            Assert.That(unload, Is.Not.Null, "No verb to take the crate off.");
            server.System<SharedVerbSystem>().ExecuteVerb(unload!, user, trolleyUid, forced: true);
        });
        await server.WaitRunTicks(Ticks(pair, 2.5));

        await server.WaitAssertion(() =>
        {
            Assert.That(Trolley(pair, trolleyUid).Comp.Cargo!.ContainedEntity, Is.Null, "The do-after did not take the crate off.");
            Assert.That(entMan.GetComponent<TransformComponent>(crate).ParentUid, Is.EqualTo(rig.Map.Grid.Owner));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crate straps onto a trolley, rides with it, and comes off again.</summary>
    [Test]
    public async Task CratesStrapToTrolleys()
    {
        await using var pair = await PoolManager.GetServerClient();
        var rig = await Build(pair);
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var tugs = server.System<WGTugSystem>();
            var trolley = Trolley(pair, rig.Trolleys[0]);
            var crate = entMan.SpawnEntity("CrateGeneric", new EntityCoordinates(rig.Map.Grid.Owner, StartX + 1, StartY + 1));
            var mob = entMan.SpawnEntity("MobHuman", new EntityCoordinates(rig.Map.Grid.Owner, StartX + 1, StartY + 2));
            var locker = entMan.SpawnEntity("LockerFreezer", new EntityCoordinates(rig.Map.Grid.Owner, StartX + 2, StartY + 2));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tugs.TryLoad(trolley, mob), Is.False, "A mob was strapped on as cargo.");
                Assert.That(tugs.TryLoad(trolley, locker), Is.False, "A locker was strapped on as cargo.");
                Assert.That(tugs.TryLoad(trolley, crate), Is.True, "The crate did not strap on.");
                Assert.That(trolley.Comp.Cargo!.ContainedEntity, Is.EqualTo(crate));
                Assert.That(entMan.GetComponent<TransformComponent>(crate).ParentUid, Is.EqualTo(trolley.Owner));
            }

            var drop = new EntityCoordinates(rig.Map.Grid.Owner, StartX + 2, StartY + 1);
            Assert.That(tugs.TryUnload(trolley, drop), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(trolley.Comp.Cargo!.ContainedEntity, Is.Null);
                Assert.That(entMan.GetComponent<TransformComponent>(crate).ParentUid, Is.EqualTo(rig.Map.Grid.Owner));
            }
        });

        await pair.CleanReturnAsync();
    }
}
