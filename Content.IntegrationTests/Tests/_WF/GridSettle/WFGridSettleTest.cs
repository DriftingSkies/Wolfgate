using System.Numerics;
using Content.Server._WF.GridSettle;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Maps;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.GridSettle;

[TestFixture]
public sealed class WFGridSettleTest
{
    /// <summary>An unattended drifting grid comes to rest and sleeps; a thrusting or piloted one keeps its speed.</summary>
    [Test]
    public async Task IdleGridsSettleAndActiveGridsKeepDrifting()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var physics = server.System<SharedPhysicsSystem>();
        var maps = server.System<SharedMapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        EntityUid idle = default, thrusting = default, piloted = default, map = default;
        await server.WaitPost(() =>
        {
            map = maps.CreateMap(out var mapId);
            var plating = new Tile(tileDefs["Plating"].TileId);

            EntityUid MakeGrid(float x)
            {
                var grid = maps.CreateGridEntity(mapId);
                server.System<SharedTransformSystem>().SetLocalPosition(grid, new Vector2(x, 0f));
                for (var tx = 0; tx < 3; tx++)
                for (var ty = 0; ty < 3; ty++)
                    maps.SetTile(grid, grid.Comp, new Vector2i(tx, ty), plating);
                physics.SetLinearVelocity(grid, new Vector2(5f, 0f));
                return grid;
            }

            idle = MakeGrid(0f);
            thrusting = MakeGrid(500f);
            piloted = MakeGrid(1000f);

            em.GetComponent<ShuttleComponent>(thrusting).ThrustDirections = DirectionFlag.North;

            var console = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(piloted, 1.5f, 1.5f));
            var pilot = em.SpawnEntity("MobHuman", new EntityCoordinates(piloted, 1.5f, 1.5f));
            // Kept conscious in vacuum, so the console doesn't drop it as a pilot.
            em.RemoveComponent<BarotraumaComponent>(pilot);
            em.RemoveComponent<RespiratorComponent>(pilot);
            em.EnsureComponent<PilotComponent>(pilot);
            server.System<ShuttleConsoleSystem>().AddPilot(console, pilot, em.GetComponent<ShuttleConsoleComponent>(console));
        });

        await server.WaitRunTicks(pair.SecondsToTicks(20f));

        await server.WaitAssertion(() =>
        {
            var idleBody = em.GetComponent<PhysicsComponent>(idle);
            Assert.That(em.HasComponent<WFGridSettleComponent>(idle), Is.True);
            Assert.That(idleBody.LinearVelocity.Length(), Is.LessThan(WFGridSettleSystem.SleepSpeed));
            Assert.That(idleBody.Awake, Is.False, "a settled grid should sleep so it stops being networked");

            foreach (var active in new[] { thrusting, piloted })
            {
                Assert.That(em.HasComponent<WFGridSettleComponent>(active), Is.False);
                Assert.That(em.GetComponent<PhysicsComponent>(active).LinearVelocity.Length(), Is.GreaterThan(1f));
            }
        });

        await server.WaitPost(() => em.DeleteEntity(map));
        await pair.CleanReturnAsync();
    }
}
