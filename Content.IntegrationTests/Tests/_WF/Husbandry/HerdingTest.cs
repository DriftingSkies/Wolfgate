#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Outposts;
using Content.Server._WF.Husbandry;
using Content.Server._WF.Husbandry.Components;
using Content.Server._WF.Outposts;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.NPC.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Husbandry;

/// <summary>An animal is lassoed, tamed by hand and whistled after, to a stop and to a pen.</summary>
[TestFixture]
[TestOf(typeof(WFHusbandrySystem))]
public sealed class HerdingTest
{
    /// <summary>
    /// A cow takes two loops of the lasso and a chicken one; the roped chicken is led along and tamed with three
    /// feedings; then the whistle has it follow its whistler, stay put, and head for the nearest pen.
    /// </summary>
    [Test]
    public async Task LassoTameAndWhistleToPen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var husbandry = server.System<WFHusbandrySystem>();
        var maps = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();
        var hands = server.System<SharedHandsSystem>();
        var htns = server.System<HTNSystem>();
        var physics = server.System<SharedPhysicsSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        const float loop = 3.5f;
        int Ticks(float seconds) => (int) (server.Timing.TickRate * seconds);

        try
        {
            EntityUid outpost = default, chicken = default, cow = default, lassoUid = default;
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                outpost = grid!.Value;
                LivestockSaveTest.BuildPen(entMan, maps, tileDefs, outpost);

                chicken = entMan.SpawnEntity("MobChicken", new EntityCoordinates(ground, 0.5f, 1.5f));
                cow = entMan.SpawnEntity("MobCow", new EntityCoordinates(ground, 1.5f, -1.5f));
                entMan.EnsureComponent<WFTameableComponent>(chicken);
                // Held still while the lasso and the feedings are worked, so a wander can't break them.
                htns.SetHTNEnabled((chicken, entMan.GetComponent<HTNComponent>(chicken)), false);
                htns.SetHTNEnabled((cow, entMan.GetComponent<HTNComponent>(cow)), false);

                lassoUid = entMan.SpawnEntity("WFLasso", entMan.GetComponent<TransformComponent>(user).Coordinates);
                Assert.That(hands.TryPickup(user, lassoUid), "The lasso is in hand.");
                var lasso = entMan.GetComponent<WFLassoComponent>(lassoUid);
                TestContext.Out.WriteLine($"Chicken {entMan.GetComponent<PhysicsComponent>(chicken).FixturesMass} kg, cow {entMan.GetComponent<PhysicsComponent>(cow).FixturesMass} kg.");
                Assert.That(husbandry.IsBig(cow, lasso), "A cow is big.");
                Assert.That(husbandry.IsBig(chicken, lasso), Is.False, "A chicken isn't.");
                Assert.That(husbandry.TryStartLasso(user, (lassoUid, lasso), cow), "The lasso is thrown at the cow.");
            });
            await pair.RunTicksSync(Ticks(loop));

            await server.WaitAssertion(() =>
            {
                var lasso = entMan.GetComponent<WFLassoComponent>(lassoUid);
                Assert.That(entMan.HasComponent<WFLassoedComponent>(cow), Is.False, "One loop doesn't hold a cow.");
                Assert.That(lasso.Looped, Is.EqualTo(cow));
                Assert.That(husbandry.TryStartLasso(user, (lassoUid, lasso), cow),
                    $"A second loop is thrown; the cow is {(transform.GetWorldPosition(cow) - transform.GetWorldPosition(user)).Length()} m off, lassoable {husbandry.CanLasso(cow)}, do-afters {entMan.GetComponent<Content.Shared.DoAfter.DoAfterComponent>(user).DoAfters.Count}.");
            });
            await pair.RunTicksSync(Ticks(loop));

            await server.WaitAssertion(() =>
            {
                var lasso = entMan.GetComponent<WFLassoComponent>(lassoUid);
                Assert.That(entMan.HasComponent<WFLassoedComponent>(cow), "Two loops hold the cow.");
                Assert.That(entMan.GetComponent<JointComponent>(cow).GetJoints.ContainsKey(WFHusbandrySystem.LassoJoint));
                husbandry.Release((lassoUid, lasso));
                Assert.That(entMan.HasComponent<WFLassoedComponent>(cow), Is.False, "Let go.");
                Assert.That(husbandry.TryStartLasso(user, (lassoUid, lasso), chicken), "The lasso is thrown at the chicken.");
            });
            await pair.RunTicksSync(Ticks(loop));

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<WFLassoedComponent>(chicken), "One loop holds a chicken.");
                // Walked off, the rope brings the chicken along. A teleport has no speed to wake the chicken's body as
                // walking would, so it is woken by hand.
                transform.SetCoordinates(user, new EntityCoordinates(ground, 0.5f, -2f));
                physics.WakeBody(chicken);
            });
            await pair.RunTicksSync(60);

            await server.WaitAssertion(() =>
            {
                var distance = (transform.GetWorldPosition(chicken) - transform.GetWorldPosition(user)).Length();
                Assert.That(distance, Is.LessThan(3.4f), "The roped chicken was led along.");
                Assert.That(server.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(chicken), "Unhurt by the rope.");
                Assert.That(entMan.HasComponent<WFLassoedComponent>(chicken), "Still on the rope.");
                transform.SetCoordinates(chicken, new EntityCoordinates(ground, 0.5f, -1f));
            });

            // Three feedings tame it.
            for (var i = 0; i < 3; i++)
            {
                await server.WaitAssertion(() =>
                {
                    var food = entMan.SpawnEntity("FoodApple", entMan.GetComponent<TransformComponent>(user).Coordinates);
                    Assert.That(husbandry.TryStartFeed(user, food, (chicken, entMan.GetComponent<WFTameableComponent>(chicken))),
                        $"Feeding {i + 1} starts; the chicken is {(transform.GetWorldPosition(chicken) - transform.GetWorldPosition(user)).Length()} m off, alive {server.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(chicken)}, fed {entMan.GetComponent<WFTameableComponent>(chicken).Fed.Count}.");
                });
                await pair.RunTicksSync(Ticks(3f));
                await server.WaitAssertion(() =>
                {
                    var tameable = entMan.GetComponent<WFTameableComponent>(chicken);
                    Assert.That(tameable.Tamed, Is.EqualTo(i == 2), $"Tamed after feeding {i + 1}: {tameable.Tamed}.");
                });
            }

            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.HasComponent<WFLivestockComponent>(chicken), "A tamed animal is livestock.");
                Assert.That(server.System<NpcFactionSystem>().IsMember(chicken, WFHusbandrySystem.LivestockFaction));
                Assert.That(entMan.GetComponent<HTNComponent>(chicken).RootTask.Task, Is.EqualTo(WFHusbandrySystem.LivestockTask.Id));

                husbandry.Release((lassoUid, entMan.GetComponent<WFLassoComponent>(lassoUid)));
                htns.SetHTNEnabled((chicken, entMan.GetComponent<HTNComponent>(chicken)), true);
                Assert.That(husbandry.Herd(user, WFHerdCommand.Follow, 12f), Is.EqualTo(1), "Only the tame chicken answers the whistle.");
                transform.SetCoordinates(user, new EntityCoordinates(ground, 10.5f, -5.5f));
            });
            await pair.RunTicksSync(Ticks(8f));

            await server.WaitAssertion(() =>
            {
                var distance = (transform.GetWorldPosition(chicken) - transform.GetWorldPosition(user)).Length();
                Assert.That(distance, Is.LessThan(4f), "The chicken followed its whistler.");

                Assert.That(husbandry.Herd(user, WFHerdCommand.Stay, 12f), Is.EqualTo(1));
                var stay = Target(chicken);
                Assert.That((transform.ToMapCoordinates(stay).Position - transform.GetWorldPosition(chicken)).Length(), Is.LessThan(0.5f),
                    "Told to stay, it keeps to where it stands.");

                Assert.That(husbandry.Herd(user, WFHerdCommand.Pen, 12f), Is.EqualTo(1));
                var pen = Target(chicken);
                Assert.That(pen.EntityId, Is.EqualTo(outpost), "Sent to the pen on the outpost.");
                Assert.That(LivestockSaveTest.InPen(entMan, maps, outpost, pen), "The spot it is sent to is inside the pen.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();

        EntityCoordinates Target(EntityUid animal)
        {
            Assert.That(entMan.GetComponent<HTNComponent>(animal).Blackboard.TryGetValue<EntityCoordinates>(NPCBlackboard.FollowTarget, out var target, entMan),
                "It has somewhere to go.");
            return target;
        }
    }
}
