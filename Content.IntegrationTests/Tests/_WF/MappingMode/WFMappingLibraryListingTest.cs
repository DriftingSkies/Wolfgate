#nullable enable
using System.Linq;
using Content.Client._WF.MappingMode;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Robust.Server.GameObjects;
using Robust.Client.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using System.Numerics;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// The real client saves its session and gets its library back over the network with the new version listed, and
/// the observer keeps moving on its paused map.
/// </summary>
[TestFixture]
public sealed class WFMappingLibraryListingTest
{
    [Test]
    public async Task SaveListsOnClientAndObserverMoves()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var mapSys = entMan.System<MapSystem>();
        var xform = entMan.System<TransformSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        await Enable(pair);
        var player = pair.Player!;

        WFMappingSession session = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(player, "test mop", out var created), Is.True);
            session = created!;
            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(0, 3).Select(x => (new Vector2i(x, 0), plating)).ToList());
        });
        await pair.RunTicksSync(10);

        await client.WaitPost(() => clientSys.Send(new WFMappingSaveRequest(string.Empty)));
        await pair.RunTicksSync(10);
        await client.WaitPost(() => clientSys.Send(new WFMappingLibraryRequest()));
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            Assert.That(clientSys.Library, Is.Not.Null, "The client never got its library.");
            Assert.That(clientSys.Library!.Maps, Has.Count.EqualTo(1));
            Assert.That(clientSys.Library.Maps[0].Name, Is.EqualTo("test mop"));
            Assert.That(clientSys.Library.Maps[0].Versions, Has.Count.EqualTo(1));
        });

        // The observer sits on a paused map and must still be moved by the server's mover.
        EntityUid observer = default;
        Vector2 before = default;
        await server.WaitAssertion(() =>
        {
            observer = player.AttachedEntity!.Value;
            Assert.That(entMan.HasComponent<WFMappingObserverComponent>(observer));
            Assert.That(mapSys.IsPaused(entMan.GetComponent<MapComponent>(session.RootMap).MapId), Is.True);
            before = xform.GetWorldPosition(observer);
        });
        await client.WaitPost(() =>
        {
            var inputMan = client.ResolveDependency<IInputManager>();
            var inputSys = client.EntMan.System<Robust.Client.GameObjects.InputSystem>();
            var timing = client.ResolveDependency<IGameTiming>();
            var funcId = inputMan.NetworkBindMap.KeyFunctionID(EngineKeyFunctions.MoveRight);
            var message = new ClientFullInputCmdMessage(timing.CurTick, timing.TickFraction, funcId)
            {
                State = BoundKeyState.Down,
            };
            inputSys.HandleInputCommand(client.Session!, EngineKeyFunctions.MoveRight, message);
        });
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
        {
            var after = xform.GetWorldPosition(observer);
            Assert.That(after.X, Is.GreaterThan(before.X + 0.1f), "The observer should have moved right on its paused map.");
        });

        // Back in the lobby, the panel on screen is the live one: the engine reuses the LobbyGui between visits.
        await client.WaitPost(() => clientSys.Send(new WFMappingLeaveRequest()));
        for (var i = 0; i < 50 && client.ResolveDependency<Robust.Client.State.IStateManager>().CurrentState is not Content.Client.Lobby.LobbyState; i++)
        {
            await pair.RunTicksSync(1);
        }
        await pair.RunTicksSync(5);
        await client.WaitPost(() => clientSys.Send(new WFMappingLibraryRequest()));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            var lobby = (Content.Client.Lobby.UI.LobbyGui) client.ResolveDependency<Robust.Client.UserInterface.IUserInterfaceManager>().ActiveScreen!;
            var panel = lobby.WFMappingPanel as WFMappingLobbyPanel;
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel!.Parent, Is.Not.Null, "The live panel sits on the lobby after a second visit.");
            Assert.That(panel.Parent!.Children.OfType<WFMappingLobbyPanel>().Count(), Is.EqualTo(1));
            Assert.That(panel.LibraryList.ChildCount, Is.EqualTo(1));
            Assert.That(panel.LibraryList.GetChild(0), Is.TypeOf<Robust.Client.UserInterface.Controls.BoxContainer>(),
                "The library list shows the saved map, not the empty label.");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
