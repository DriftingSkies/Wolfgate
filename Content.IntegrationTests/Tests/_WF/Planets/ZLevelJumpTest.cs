using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Planets;

/// <summary>
/// Bug report 51: jump boots moved a player a third of a tile. Every species can move in air for z-levels, so
/// walking friction stopped the throw, and on a z-level the z-physics also set the body back on the ground.
/// </summary>
[TestFixture]
public sealed class ZLevelJumpTest
{
    [Test]
    public async Task JumpBootsCarryTheJumpOnAZLevel()
    {
        await using var pair = await PoolManager.GetServerClient();
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        await LayTiles(pair, layers[0], new Vector2i(-12, -12), new Vector2i(12, 12));

        await AssertJumpCarries(pair, new EntityCoordinates(layers[0], new Vector2(0.5f)));

        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task JumpBootsCarryTheJumpOnAShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var em = server.EntMan;

        await server.WaitPost(() =>
        {
            em.EnsureComponent<GravityComponent>(map.Grid).Enabled = true;
            var floor = map.Tile.Tile;
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = -12; x <= 12; x++)
            for (var y = -12; y <= 12; y++)
            {
                tiles.Add((new Vector2i(x, y), floor));
            }

            server.System<SharedMapSystem>().SetTiles(map.Grid, map.Grid.Comp, tiles);
        });

        await AssertJumpCarries(pair, new EntityCoordinates(map.Grid, new Vector2(0.5f)));
        await pair.CleanReturnAsync();
    }

    /// <summary>Jumps a booted human from <paramref name="at"/> and asserts it covers most of the boots' distance.</summary>
    private static async Task AssertJumpCarries(TestPair pair, EntityCoordinates at)
    {
        var server = pair.Server;
        var em = server.EntMan;
        var xforms = server.System<SharedTransformSystem>();
        var inventory = server.System<InventorySystem>();
        EntityUid mob = default;
        EntityUid boots = default;
        Vector2 start = default;

        await server.WaitPost(() =>
        {
            mob = em.SpawnEntity("MobHuman", at);
            boots = em.SpawnEntity("ClothingShoesBootsJump", at);
            Assert.That(inventory.TryEquip(mob, boots, "shoes", force: true), Is.True);
        });
        await pair.RunTicksSync(30);

        await server.WaitPost(() =>
        {
            Assert.That(server.System<SharedGravitySystem>().IsWeightless(mob), Is.False,
                "Precondition: no gravity, so the boots would not jump.");
            start = xforms.GetWorldPosition(mob);
            var jump = new GravityJumpEvent { Performer = mob };
            em.EventBus.RaiseLocalEvent(boots, jump);
            Assert.That(jump.Handled, Is.True, "Precondition: the boots did not jump.");
        });
        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var distance = (xforms.GetWorldPosition(mob) - start).Length();
            var expected = em.GetComponent<JumpAbilityComponent>(boots).JumpDistance;
            Assert.That(distance, Is.GreaterThanOrEqualTo(expected * 0.75f),
                $"The jump carried {distance:F2} of {expected} tiles.");
        });
    }
}
