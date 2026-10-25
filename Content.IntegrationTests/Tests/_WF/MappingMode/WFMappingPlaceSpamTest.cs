#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Robust.Server.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// Dragging the engine's tile placement off a grid, as the real client sends it, extends that grid instead of making
/// one grid per tile, for mappers and for admins, who skip the placement gate; a wall placed on a wall replaces it.
/// </summary>
[TestFixture]
public sealed class WFMappingPlaceSpamTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task DraggedTilesJoinTheGrid(bool admin)
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        await Enable(pair, demoteClient: !admin);
        var player = pair.Player!;
        var plating = new Tile(tileDefs["Plating"].TileId);

        WFMappingSession session = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, "Place Spam", out var created), Is.True);
            session = created!;
            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(0, 3).Select(x => (new Vector2i(x, 0), plating)).ToList());
        });
        await pair.RunTicksSync(10);

        NetEntity netMap = default;
        await server.WaitPost(() => netMap = entMan.GetNetEntity(session.RootMap));

        // One cell a tick along the row past the grid's end, in map coordinates like a drag off every grid.
        for (var x = 3; x < 13; x++)
        {
            var cell = x;
            await client.WaitPost(() => clientNet.ClientSendMessage(new MsgPlacement
            {
                PlaceType = PlacementManagerMessage.RequestPlacement,
                IsTile = true,
                TileType = plating.TypeId,
                NetCoordinates = new NetCoordinates(netMap, new Vector2(cell + 0.5f, 0.5f)),
                Align = "None",
            }));
            await pair.RunTicksSync(2);
        }

        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var grids = mapping.GridsOn(session.RootMap).ToList();
            Assert.That(grids, Has.Count.EqualTo(1), "The dragged tiles should all join the one grid.");
            var grid = entMan.GetEntity(grids[0].Grid);
            Assert.That(mapSys.GetAllTiles(grid, entMan.GetComponent<MapGridComponent>(grid)).Count(), Is.EqualTo(13));
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WallOnWallReplaces()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        await Enable(pair);
        var player = pair.Player!;

        Entity<MapGridComponent> grid = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, "Wall Replace", out var session), Is.True);
            grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session!.RootMap).MapId);
            mapSys.SetTile(grid, grid.Comp, Vector2i.Zero, new Tile(tileDefs["Plating"].TileId));
        });
        await pair.RunTicksSync(10);

        NetEntity netGrid = default;
        await server.WaitPost(() => netGrid = entMan.GetNetEntity(grid.Owner));
        foreach (var wall in new[] { "WallSolid", "WallReinforced" })
        {
            await client.WaitPost(() => clientNet.ClientSendMessage(new MsgPlacement
            {
                PlaceType = PlacementManagerMessage.RequestPlacement,
                EntityTemplateName = wall,
                NetCoordinates = new NetCoordinates(netGrid, new Vector2(0.5f, 0.5f)),
                Align = "None",
                Replacement = true,
            }));
            await pair.RunTicksSync(5);
        }

        await server.WaitAssertion(() =>
        {
            var walls = mapSys.GetAnchoredEntities(grid.Owner, grid.Comp!, Vector2i.Zero)
                .Select(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID)
                .ToList();
            Assert.That(walls, Is.EqualTo(new[] { "WallReinforced" }), "The second wall replaces the first.");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
