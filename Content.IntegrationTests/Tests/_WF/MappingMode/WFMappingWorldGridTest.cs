#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared._WF.Planets;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network.Messages;
using Robust.Shared.Placement;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// Session grids stay on whole world tiles, tiles laid off every grid join the nearest grid unless the mapper asks
/// for a new one, grids don't split, and the grid list covers every level.
/// </summary>
[TestFixture]
public sealed class WFMappingWorldGridTest
{
    [Test]
    public async Task WorldGrid()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var cfg = server.CfgMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var tools = entMan.System<MappingToolsSystem>();
        var zLevels = entMan.System<MappingZLevelsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var xformSys = entMan.System<TransformSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        WFMappingSession session = default!;
        EntityUid grid = default;
        var plating = new Tile(tileDefs["Plating"].TileId);

        List<EntityUid> Grids() => mapping.GridsOn(session.RootMap).Select(g => entMan.GetEntity(g.Grid)).ToList();

        bool HasTile(EntityUid uid, Vector2 world) =>
            mapSys.TryGetTileRef(uid, entMan.GetComponent<MapGridComponent>(uid),
                mapSys.TileIndicesFor(uid, entMan.GetComponent<MapGridComponent>(uid),
                    new MapCoordinates(world, entMan.GetComponent<MapComponent>(session.RootMap).MapId)),
                out var tile) && !tile.Tile.IsEmpty;

        MsgPlacement PlaceTile(Vector2 world) => new()
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            IsTile = true,
            TileType = plating.TypeId,
            NetCoordinates = entMan.GetNetCoordinates(new EntityCoordinates(session.RootMap, world)),
            Align = "None",
        };

        await server.WaitAssertion(() =>
        {
            Assert.That(cfg.GetCVar(CVars.GridSplitting), Is.False, "Mapping mode turns grid splitting off.");
            Assert.That(mapping.TryCreateSession(a, "World Grid Test", out var created), Is.True);
            session = created!;

            var newGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            grid = newGrid.Owner;
            mapSys.SetTiles(grid, newGrid.Comp,
                Enumerable.Range(0, 3).Select(x => (new Vector2i(x, 0), plating)).ToList());
            xformSys.SetLocalPositionRotation(grid, new Vector2(3.4f, -2.7f), Angle.FromDegrees(3));
        });

        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(grid);
            Assert.Multiple(() =>
            {
                Assert.That(Vector2.Distance(xform.LocalPosition, new Vector2(3, -3)), Is.LessThan(0.001f),
                    "An off-tile grid is snapped.");
                Assert.That(xform.LocalRotation.Theta, Is.EqualTo(0).Within(0.001));
            });

            // Erasing the middle tile leaves two islands on one grid.
            mapSys.SetTile(grid, entMan.GetComponent<MapGridComponent>(grid), new Vector2i(1, 0), Tile.Empty);
        });

        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(Grids(), Is.EquivalentTo(new[] { grid }), "The grid doesn't split.");

            // An engine tile placement in open space joins the nearest grid on that world tile.
            var msg = PlaceTile(new Vector2(20.3f, 10.8f));
            Assert.That(mapping.AllowPlacement(a, msg), Is.True);
            var coords = entMan.GetCoordinates(msg.NetCoordinates);
            Assert.Multiple(() =>
            {
                Assert.That(coords.EntityId, Is.EqualTo(grid));
                Assert.That(Vector2.Distance(xformSys.ToMapCoordinates(coords).Position, new Vector2(20.5f, 10.5f)),
                    Is.LessThan(0.001f));
            });

            // Tools drawn in open space join it too.
            var draw = new MappingToolsDrawEvent(null, MappingDrawOp.SetTiles, new List<Vector2i> { new(-10, 5) },
                "Plating", 0, null, Angle.Zero) { Map = entMan.GetNetEntity(session.RootMap) };
            tools.Draw(a, draw);
            Assert.Multiple(() =>
            {
                Assert.That(Grids(), Is.EquivalentTo(new[] { grid }));
                Assert.That(HasTile(grid, new Vector2(-9.5f, 5.5f)), Is.True, "The drawn tile is on the grid.");
            });

            // Asking for a new grid sends the next placement to the map, centred on a whole tile, once.
            RaiseAs(entMan, a, new WFMappingNewGridRequest(true));
            var fresh = PlaceTile(new Vector2(-20.9f, -20.2f));
            Assert.That(mapping.AllowPlacement(a, fresh), Is.True);
            var freshCoords = entMan.GetCoordinates(fresh.NetCoordinates);
            Assert.Multiple(() =>
            {
                Assert.That(freshCoords.EntityId, Is.EqualTo(session.RootMap));
                Assert.That(Vector2.Distance(freshCoords.Position, new Vector2(-20.5f, -20.5f)), Is.LessThan(0.001f));
            });

            var after = PlaceTile(new Vector2(-30.5f, -30.5f));
            Assert.That(mapping.AllowPlacement(a, after), Is.True);
            Assert.That(entMan.GetCoordinates(after.NetCoordinates).EntityId, Is.EqualTo(grid),
                "The new grid request is used up.");

            // Depth perspective draws a level like a planet layer.
            Assert.That(WFPlanetView.Shrink(entMan, session.RootMap), Is.EqualTo(1f));
            entMan.AddComponent<WFMappingPerspectiveComponent>(session.RootMap);
            Assert.That(WFPlanetView.Shrink(entMan, session.RootMap), Is.EqualTo(WFPlanetView.LayerShrink));
            entMan.RemoveComponent<WFMappingPerspectiveComponent>(session.RootMap);

            // The grid list covers every level.
            Assert.That(zLevels.TryAddLevel(session.RootMap, true, out var upper, out var depth, out var error), Is.True,
                error);
            var upperGrid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(upper).MapId);
            mapSys.SetTile(upperGrid.Owner, upperGrid.Comp, Vector2i.Zero, plating);
            var listed = mapping.GridsOfSession(session);
            Assert.Multiple(() =>
            {
                Assert.That(listed.Select(g => entMan.GetEntity(g.Grid)),
                    Is.EquivalentTo(new[] { grid, upperGrid.Owner }));
                Assert.That(listed[0].Grid, Is.EqualTo(entMan.GetNetEntity(upperGrid.Owner)), "Top level first.");
                Assert.That(listed[0].Depth, Is.EqualTo(depth));
            });
        });

        await Disable(pair);
        await server.WaitAssertion(() =>
            Assert.That(cfg.GetCVar(CVars.GridSplitting), Is.True, "Turning the mode off puts splitting back."));
        await pair.CleanReturnAsync();
    }
}
