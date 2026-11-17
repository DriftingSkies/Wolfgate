#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WG.MappingMode;
using Content.Server._WG.MappingMode;
using Content.Shared._WG.MappingMode;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WG.MappingMode.WGMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WG.MappingMode;

/// <summary>
/// The admin commands list, kick from and close sessions (saving them first), and an admin observes from a private
/// map that the client knows to offer Return to lobby on.
/// </summary>
[TestFixture]
public sealed class WGMappingAdminTest
{
    [Test]
    public async Task CommandsAndObserve()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WGMappingModeSystem>();
        var mapSys = entMan.System<MapSystem>();
        var res = server.ResolveDependency<IResourceManager>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var clientMapping = client.EntMan.System<WGMappingClientSystem>();

        // The test client stays host for the observe part; the dummies are plain mappers.
        await Enable(pair, demoteClient: false);
        var players = await AddMappers(pair, 2);
        var (a, b) = (players[0], players[1]);

        WGMappingSession session = default!;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(a, "Admin Test", out var created), Is.True);
            session = created!;
            Assert.That(mapping.TryJoinSession(b, session), Is.True);
            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            mapSys.SetTile(grid, grid.Comp, Vector2i.Zero, new Tile(tileDefs["Plating"].TileId));
            Assert.That(session.Dirty, Is.True);
        });

        await pair.WaitCommand("wg_mappingmode_list");

        // Kick: the member goes back to the lobby and out of the session.
        await pair.WaitCommand($"wg_mappingmode_kick {b.Name}");
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(mapping.TryGetMembership(b.UserId, out _), Is.False);
                Assert.That(b.AttachedEntity, Is.Null);
                Assert.That(session.Members.Keys, Is.EquivalentTo(new[] { a.UserId }));
            });
        });

        // Close: autosaved into the owner's library first, then gone with its maps.
        var root = session.RootMap;
        await pair.WaitCommand($"wg_mappingmode_close {session.Code}");
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(mapping.Sessions, Does.Not.Contain(session));
                Assert.That(a.AttachedEntity, Is.Null);
                Assert.That(entMan.Deleted(root), Is.True);
            });

            var folder = WGMappingLibraryNames.MapFolder(a.UserId, "Admin Test");
            Assert.That(res.UserData.IsDir(folder), Is.True, "Closing saves the changed session.");
            var versions = res.UserData.DirectoryEntries(folder).ToList();
            Assert.That(versions, Has.Count.EqualTo(1));
            Assert.That(WGMappingLibraryNames.TryParseVersion(versions[0], out var version) &&
                        version.Kind == WGMappingVersionKind.Auto, Is.True);
        });

        // Observe: an admin observer on the private admin map, and the client is told so.
        var player = pair.Player!;
        await server.WaitPost(() => RaiseAs(entMan, player, new WGMappingAdminObserveRequest()));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(player.AttachedEntity, Is.Not.Null);
            var map = entMan.GetComponent<TransformComponent>(player.AttachedEntity!.Value).MapUid;
            Assert.That(map, Is.Not.Null);
            Assert.That(entMan.TryGetComponent(map, out WGMappingSessionMapComponent? tag), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(tag!.Kind, Is.EqualTo(WGMappingMapKind.Admin));
                Assert.That(tag.SessionId, Is.Zero);
                Assert.That(mapping.IsAdminObserver(player), Is.True);
            });
        });
        await client.WaitAssertion(() => Assert.That(clientMapping.AdminObserver, Is.True));

        await server.WaitPost(() => RaiseAs(entMan, player, new WGMappingAdminLeaveRequest()));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() => Assert.That(player.AttachedEntity, Is.Null));
        await client.WaitAssertion(() => Assert.That(clientMapping.AdminObserver, Is.False));

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
