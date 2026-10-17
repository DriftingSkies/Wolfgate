using System.Collections.Generic;
using System.Numerics;
using Content.Server.Projectiles;
using Content.Shared._Mono.CCVar;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.Weapons;

/// <summary>
/// The sweep that keeps fast projectiles from tunnelling: what it leaves alone, and the speed it starts at.
/// </summary>
[TestFixture]
public sealed class ProjectileSweepTest
{
    private const string Arrow = "ArrowRegular";

    /// <summary>
    /// An arrow that has not been shot is an item. Carried on a fast ship it used to be swept as a round in
    /// flight: pulled out of the hand holding it, or dragged to the nearest wall and thrown when the ship slowed.
    /// </summary>
    [Test]
    public async Task UnfiredArrowsAreNotSwept()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var maps = server.System<SharedMapSystem>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var threshold = config.GetCVar(MonoCVars.ProjectileRaycastSpeedThreshold);

        EntityUid grid = default, holder = default, held = default, loose = default;
        var rest = Vector2.Zero;

        try
        {
            await server.WaitPost(() =>
            {
                // Any ship counts as fast, so the test does not need one doing 75 m/s.
                config.SetCVar(MonoCVars.ProjectileRaycastSpeedThreshold, 1f);
                entities.DeleteEntity(map.Grid);
                grid = MakeGrid(entities, maps, map.MapId, 5);
                for (var y = 0; y < 5; y++)
                {
                    entities.SpawnEntity("WallSolid", new EntityCoordinates(grid, new Vector2(0.5f, y + 0.5f)));
                }

                var middle = new EntityCoordinates(grid, new Vector2(3.5f, 2.5f));
                holder = entities.SpawnEntity("MobHuman", middle);
                held = entities.SpawnEntity(Arrow, middle);
                // Lying just short of the wall the ship is about to move towards.
                loose = entities.SpawnEntity(Arrow, new EntityCoordinates(grid, new Vector2(1.25f, 1.5f)));
                Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(holder, held), Is.True);
            });

            await server.WaitRunTicks(5);
            await server.WaitPost(() =>
            {
                rest = entities.GetComponent<TransformComponent>(loose).LocalPosition;
                entities.System<SharedPhysicsSystem>().SetLinearVelocity(grid, new Vector2(-9f, 0f));
            });

            await server.WaitRunTicks(30);
            await server.WaitPost(() => entities.System<SharedPhysicsSystem>().SetLinearVelocity(grid, Vector2.Zero));
            await server.WaitRunTicks(30);

            await server.WaitAssertion(() =>
            {
                var xform = entities.GetComponent<TransformComponent>(loose);
                Assert.Multiple(() =>
                {
                    Assert.That(entities.System<SharedContainerSystem>().IsEntityInContainer(held), Is.True,
                        "A held arrow stays in the hand while the ship moves.");
                    Assert.That(xform.ParentUid, Is.EqualTo(grid));
                    Assert.That((xform.LocalPosition - rest).Length(), Is.LessThan(0.05f),
                        "A loose arrow stays where it was put down.");
                    Assert.That(entities.GetComponent<ProjectileComponent>(held).RaycastResetVelocity, Is.Null);
                    Assert.That(entities.GetComponent<ProjectileComponent>(loose).RaycastResetVelocity, Is.Null);
                });

                entities.DeleteEntity(holder);
                entities.DeleteEntity(grid);
            });
        }
        finally
        {
            await server.WaitPost(() => config.SetCVar(MonoCVars.ProjectileRaycastSpeedThreshold, threshold));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Half the physics tickrate halves the speed the sweep starts at. A whole-number division used to turn
    /// anything under 60 into no threshold at all, so every projectile was swept at any speed.
    /// </summary>
    [Test]
    public async Task ThresholdScalesWithThePhysicsTickrate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var config = server.ResolveDependency<IConfigurationManager>();
        var tickrate = config.GetCVar(CVars.TargetMinimumTickrate);
        var adaptive = config.GetCVar(MonoCVars.ProjectileAdaptiveRaycastThreshold);
        var threshold = config.GetCVar(MonoCVars.ProjectileRaycastSpeedThreshold);

        try
        {
            await server.WaitAssertion(() =>
            {
                var projectiles = entities.System<ProjectileSystem>();
                config.SetCVar(MonoCVars.ProjectileAdaptiveRaycastThreshold, true);

                config.SetCVar(CVars.TargetMinimumTickrate, 60);
                Assert.Multiple(() =>
                {
                    Assert.That(projectiles.ShouldRaycastProjectile(threshold * 0.9f), Is.False);
                    Assert.That(projectiles.ShouldRaycastProjectile(threshold * 1.1f), Is.True);
                });

                config.SetCVar(CVars.TargetMinimumTickrate, 30);
                Assert.Multiple(() =>
                {
                    Assert.That(projectiles.ShouldRaycastProjectile(threshold * 0.4f), Is.False,
                        "Below half the threshold nothing is swept at half the tickrate.");
                    Assert.That(projectiles.ShouldRaycastProjectile(threshold * 0.6f), Is.True,
                        "Half the tickrate sweeps from half the speed.");
                });
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                config.SetCVar(CVars.TargetMinimumTickrate, tickrate);
                config.SetCVar(MonoCVars.ProjectileAdaptiveRaycastThreshold, adaptive);
                config.SetCVar(MonoCVars.ProjectileRaycastSpeedThreshold, threshold);
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A free-floating square hull.</summary>
    private static EntityUid MakeGrid(IEntityManager entities, SharedMapSystem maps, MapId map, int size)
    {
        var grid = maps.CreateGridEntity(map);
        var tiles = new List<(Vector2i GridIndices, Tile Tile)>();
        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                tiles.Add((new Vector2i(x, y), new Tile(1)));
            }
        }

        entities.System<SharedMapSystem>().SetTiles(grid.Owner, grid.Comp, tiles);
        entities.System<SharedPhysicsSystem>().SetBodyType(grid.Owner, BodyType.Dynamic);
        return grid.Owner;
    }
}
