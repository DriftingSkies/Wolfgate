#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.RMCCore.Actions;
using Content.Shared._WF.Xenonids.Construction.Events;
using Content.Shared._WF.Xenonids.Eye;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The queen's eye sees on a grid that is far from the origin and rotated, and swapping her weeds action keeps the button.</summary>
[TestFixture]
public sealed class XenoQueenEyeTest
{
    [Test]
    public async Task EyeSeesOnMovedRotatedGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var xforms = entities.System<SharedTransformSystem>();
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);

            // a ship: far from the map origin and turned
            xforms.SetWorldPositionRotation(map.Grid, new Vector2(500, -800), Angle.FromDegrees(37));

            var queen = entities.SpawnEntity("CMXenoQueen", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            Assert.That(entities.HasComponent<QueenEyeVisionComponent>(queen), "The queen is a vision seed.");

            var worldBounds = new Box2Rotated(Box2.CenteredAround(xforms.GetWorldPosition(queen), new Vector2(20, 20)), Angle.Zero);
            var visible = new HashSet<Vector2i>();
            entities.System<QueenEyeSystem>().GetView(
                (map.Grid.Owner, entities.GetComponent<BroadphaseComponent>(map.Grid), entities.GetComponent<MapGridComponent>(map.Grid)),
                worldBounds,
                visible);

            Assert.That(visible, Does.Contain(new Vector2i(0, 0)), "The queen's own tile is visible to her eye on a moved, rotated grid.");
            Assert.That(visible, Does.Contain(new Vector2i(3, 3)));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SwappingPlantWeedsKeepsTheButton()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var actions = entities.System<SharedActionsSystem>();
            var swap = entities.System<SwappableActionSystem>();
            var queen = entities.SpawnEntity("CMXenoQueen", map.GridCoords);

            EntityUid Weeds() => entities.System<SharedRMCActionsSystem>().GetActionsWithEvent<XenoPlantWeedsActionEvent>(queen).Select(a => a.Owner).FirstOrDefault();
            var before = Weeds();
            Assert.That(before.IsValid(), "The queen starts with a plant weeds action.");

            Assert.That(swap.SwapInstantToWorldTarget<XenoPlantWeedsActionEvent>(queen, "expand", "expand weeds"), "The weeds action swaps to a world target.");
            Assert.That(actions.GetActions(queen).Select(a => a.Id), Does.Contain(before), "The swapped action is still on the queen's bar.");
            Assert.That(entities.HasComponent<WorldTargetActionComponent>(before));

            swap.SwapAllToInstant(queen);
            Assert.That(actions.GetActions(queen).Select(a => a.Id), Does.Contain(before), "The action comes back as an instant one and stays on the bar.");
            Assert.That(entities.HasComponent<InstantActionComponent>(before));
        });

        await pair.CleanReturnAsync();
    }
}
