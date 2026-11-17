using System.Collections.Generic;
using Content.Client._WG.MappingMode;
using Content.Client._WG.MappingMode.Link;
using Content.Client._WG.MappingMode.Screen;
using Content.Server._WG.MappingMode;
using Content.Shared._WG.MappingMode;
using Content.Shared.DeviceLinking;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WG.MappingMode.WGMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WG.MappingMode;

/// <summary>
/// The Links layer on the real client: switching it on asks the server for the level's signal links, the snapshot
/// comes back for the viewed level, the overlay is up and the count the Layers panel shows matches.
/// </summary>
[TestFixture]
public sealed class WGMappingLinksLayerTest
{
    [Test]
    public async Task LinksLayerShowsTheLevelsLinks()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WGMappingModeSystem>();
        var links = entMan.System<SharedDeviceLinkSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var clientSys = client.EntMan.System<WGMappingClientSystem>();
        var clientLinks = client.EntMan.System<WGMappingLinkSystem>();
        var overlays = client.ResolveDependency<IOverlayManager>();
        var uiMan = client.ResolveDependency<IUserInterfaceManager>();
        await Enable(pair);

        await client.WaitPost(() => clientSys.Send(new WGMappingCreateRequest("links test")));
        for (var i = 0; i < 100 && uiMan.ActiveScreen is not WGMappingScreen; i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(uiMan.ActiveScreen, Is.TypeOf<WGMappingScreen>(), "The client is in a session.");

        EntityUid rootMap = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryGetMembership(pair.Player!.UserId, out var session), Is.True);
            rootMap = session!.RootMap;
            var plating = new Tile(tileDefs["Plating"].TileId);
            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(rootMap).MapId);
            mapSys.SetTiles(grid, grid.Comp, new List<(Vector2i, Tile)>
            {
                (new Vector2i(0, 0), plating), (new Vector2i(1, 0), plating), (new Vector2i(2, 0), plating),
            });
            var button = entMan.SpawnEntity("SignalButton", new EntityCoordinates(grid, 0.5f, 0.5f));
            var door = entMan.SpawnEntity("Airlock", new EntityCoordinates(grid, 2.5f, 0.5f));
            links.LinkDefaults(null, button, door);
            Assert.That(entMan.GetComponent<DeviceLinkSourceComponent>(button).LinkedPorts, Has.Count.EqualTo(1),
                "The button is linked to the door.");
        });

        await pair.RunTicksSync(10);
        Assert.That(clientLinks.ShowAll, Is.False, "The Links layer starts off.");
        Assert.That(overlays.HasOverlay<WGMappingLinkOverlay>(), Is.False);

        await client.WaitPost(() => clientSys.SetLayer(WGMappingLayer.Links, true));
        for (var i = 0; i < 60 && clientLinks.Snapshot.Count == 0; i++)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(clientLinks.ShowAll, Is.True);
                Assert.That(overlays.HasOverlay<WGMappingLinkOverlay>(), Is.True, "The overlay is up.");
                Assert.That(clientLinks.Snapshot, Has.Count.EqualTo(1), "The level's one link arrived.");
                Assert.That(client.EntMan.GetNetEntity(clientLinks.SnapshotMap), Is.EqualTo(entMan.GetNetEntity(rootMap)),
                    "The snapshot is for the session's level.");
                Assert.That(clientLinks.CountOnViewedLevel().Links, Is.EqualTo(1), "The Layers panel counts it.");
            });
        });

        await client.WaitPost(() => clientSys.SetLayer(WGMappingLayer.Links, false));
        await pair.RunTicksSync(2);
        Assert.That(overlays.HasOverlay<WGMappingLinkOverlay>(), Is.False, "Off takes the overlay down.");

        await server.WaitPost(() => mapping.LeaveSession(pair.Player!.UserId, kicked: false));
        await pair.RunTicksSync(10);
        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
