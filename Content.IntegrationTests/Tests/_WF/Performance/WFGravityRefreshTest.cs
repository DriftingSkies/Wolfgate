using System.Numerics;
using Content.Shared.Gravity;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>Checks the batched weightlessness refresh after a grid's gravity changes.</summary>
[TestFixture]
[TestOf(typeof(SharedGravitySystem))]
public sealed class WFGravityRefreshTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFGravityRefreshDummy
  components:
  - type: Physics
    bodyType: Dynamic
  - type: GravityAffected

- type: entity
  id: WFGravityRefreshGenerator
  components:
  - type: GravityGenerator
  - type: PowerCharge
    windowTitle: gravity-generator-window-title
    idlePower: 50
    chargeRate: 1000000000
    activePower: 500
  - type: ApcPowerReceiver
    needsPower: false
  - type: UserInterface
";

    /// <summary>Gravity reaches the grid's entities after the batch delay, and never another grid on the same map.</summary>
    [Test]
    public async Task RefreshesOnlyTheChangedGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var gravity = server.System<SharedGravitySystem>();
        var maps = server.System<SharedMapSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid onGrid = default;
        EntityUid otherGrid = default;
        await server.WaitPost(() =>
        {
            var grid = maps.CreateGridEntity(testMap.MapId);
            entMan.System<SharedTransformSystem>().SetWorldPosition(grid.Owner, new Vector2(500f, 500f));
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, testMap.Tile.Tile);
            entMan.EnsureComponent<GravityComponent>(grid);

            onGrid = entMan.SpawnEntity("WFGravityRefreshDummy", testMap.GridCoords);
            otherGrid = entMan.SpawnEntity("WFGravityRefreshDummy", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
        });
        await pair.RunTicksSync(30);

        await server.WaitPost(() =>
        {
            Assert.That(gravity.IsWeightless(onGrid), "No generator yet.");
            entMan.SpawnEntity("WFGravityRefreshGenerator", testMap.GridCoords);
        });
        var gridGravity = entMan.GetComponent<GravityComponent>(testMap.Grid);
        for (var i = 0; i < 60 && !gridGravity.Enabled; i++)
        {
            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(gridGravity.Enabled, "The generator should have switched on.");
            Assert.That(gravity.IsWeightless(onGrid), "The refresh should wait for the batch delay.");
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(gravity.IsWeightless(onGrid), Is.False);
            Assert.That(gravity.IsWeightless(otherGrid), "The other grid has no generator.");
        });

        await pair.CleanReturnAsync();
    }
}
