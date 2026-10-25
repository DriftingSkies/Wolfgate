#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.MappingMode;
using Content.Client._WF.MappingMode.Screen;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingMode.Test;
using Content.Server._WF.MappingTools;
using Content.Server.Communications;
using Content.Server.Radio;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingMode.Test;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Radio;
using Robust.Server.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Placement;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// Live testing: starting a test puts the mapper on a humanoid on an initialised copy of their grid, with the range
/// beside it, while their own map stays paused and uninitialised; they can't edit anything from the test; stopping
/// takes them back to the same observer and deletes the test maps; a save during the test holds only the session's
/// own level.
/// </summary>
[TestFixture]
public sealed class WFMappingTestModeTest
{
    private const string MapName = "Test Mode Ship";

    private static List<string> Versions(IResourceManager res, NetUserId user, string map)
    {
        var folder = WFMappingLibraryNames.MapFolder(user, map);
        return res.UserData.IsDir(folder) ? res.UserData.DirectoryEntries(folder).Order().ToList() : new List<string>();
    }

    private static int Grids(IEntityManager entMan, EntityUid map)
    {
        var count = 0;
        var children = entMan.GetComponent<TransformComponent>(map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (entMan.HasComponent<MapGridComponent>(child))
                count++;
        }

        return count;
    }

    [Test]
    public async Task TestRoundTrip()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var testing = entMan.System<WFMappingTestSystem>();
        var zLevels = entMan.System<MappingZLevelsSystem>();
        var protection = entMan.System<MappingProtectionSystem>();
        var mapSys = entMan.System<MapSystem>();
        var library = entMan.System<WFMappingLibrarySystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var res = server.ResolveDependency<IResourceManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        WFMappingSession session = default!;
        EntityUid observer = default;
        EntityUid ownGrid = default;
        EntityUid mob = default;
        EntityUid testMap = default;
        List<EntityUid> testMaps = new();
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WFMappingModeCVars.TestCooldown, 0f);

            // A two-tile grid holding a shuttle console; the player should land in front of the console.
            Assert.That(mapping.TryCreateSession(a, MapName, out var created), Is.True);
            session = created!;
            observer = a.AttachedEntity!.Value;
            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            ownGrid = grid.Owner;
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, new List<(Vector2i, Tile)> { (new Vector2i(0, 0), plating), (new Vector2i(0, 1), plating) });
            entMan.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid, 0.5f, 1.5f));

            // An ordinary comms console announces to the whole server; its running copy must not.
            var liveComms = entMan.SpawnEntity("ComputerComms", new EntityCoordinates(grid, 0.5f, 1.5f));
            Assert.That(entMan.GetComponent<CommunicationsConsoleComponent>(liveComms).Global, Is.True);

            Assert.That(testing.TryStart(a), Is.True, "The test should start.");
            Assert.That(session.Test, Is.Not.Null);
            Assert.That(mapping.IsTesting(a), Is.True);
            Assert.That(a.AttachedEntity, Is.Not.Null.And.Not.EqualTo(observer), "The player should be on a new entity.");
            mob = a.AttachedEntity!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<HumanoidAppearanceComponent>(mob), "The tester is a humanoid.");
                Assert.That(entMan.HasComponent<WFMappingTesterComponent>(mob), "The tester is tagged.");
                Assert.That(protection.IsProtectedSelf(mob), "Edits never touch the tester.");
                Assert.That(entMan.Deleted(observer), Is.False, "The observer is kept for the return.");
                Assert.That(session.Present[a.UserId], Is.EqualTo(observer), "The member stays present.");
            });

            var xform = entMan.GetComponent<TransformComponent>(mob);
            Assert.That(xform.MapUid, Is.Not.Null);
            testMap = xform.MapUid!.Value;
            testMaps = session.Test!.Maps.ToList();
            Assert.Multiple(() =>
            {
                Assert.That(testMaps, Does.Contain(testMap));
                Assert.That(mapping.IsTestMap(testMap, session), "The tester stands on a test level.");
                Assert.That(mapping.IsSessionMap(testMap, session), Is.False, "A test level isn't a session level.");
                Assert.That(xform.GridUid, Is.Not.Null, "The tester stands on a grid.");
                Assert.That(xform.GridUid, Is.Not.EqualTo(ownGrid), "The grid is a copy, not the live one.");
                Assert.That(xform.GridUid, Is.EqualTo(session.Test.Ship));
                Assert.That(mapSys.IsInitialized(testMap), "The test level runs.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(testMap).EntityPaused, Is.False);
                Assert.That(mapSys.IsInitialized(session.RootMap), Is.False, "The session's own map stays as it was.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(session.RootMap).EntityPaused, Is.True);
                Assert.That(Grids(entMan, testMap), Is.EqualTo(2), "The ship copy and the range.");
                Assert.That(Grids(entMan, session.RootMap), Is.EqualTo(1));
                Assert.That(zLevels.GetLevels(session.RootMap, out _), Is.EquivalentTo(new[] { session.RootMap }),
                    "The test stack isn't part of the session's stack.");
                Assert.That(entMan.HasComponent<Content.Server.Shuttles.Components.ShuttleComponent>(session.Test.Ship!.Value),
                    "The copy flies.");
                Assert.That(entMan.GetComponent<MetaDataComponent>(session.Test.Ship.Value).EntityName, Is.EqualTo(MapName));
                var local = entMan.GetComponent<TransformComponent>(mob).LocalPosition;
                Assert.That((local - new System.Numerics.Vector2(0.5f, 0.5f)).Length(), Is.LessThan(0.01f),
                    "The tester lands on the tile in front of the console.");
            });

            // Nothing of the running copy reaches past the test: no global announcements, no FTL, no radio across.
            var ship = session.Test!.Ship!.Value;
            var shuttle = entMan.System<ShuttleSystem>();
            var common = server.ProtoMan.Index<RadioChannelPrototype>("Common");
            var outward = new RadioReceiveAttemptEvent(common, mob, ownGrid);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref outward);
            var inward = new RadioReceiveAttemptEvent(common, ownGrid, mob);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref inward);
            var within = new RadioReceiveAttemptEvent(common, mob, ship);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref within);
            Assert.Multiple(() =>
            {
                var copies = entMan.AllEntities<CommunicationsConsoleComponent>()
                    .Where(c => entMan.GetComponent<TransformComponent>(c).MapUid == testMap)
                    .ToList();
                Assert.That(copies, Has.Count.EqualTo(1));
                Assert.That(entMan.GetComponent<CommunicationsConsoleComponent>(copies[0]).Global, Is.False,
                    "The copy's comms console announces to no one.");
                Assert.That(entMan.GetComponent<CommunicationsConsoleComponent>(liveComms).Global, Is.True,
                    "The live console is untouched.");
                Assert.That(shuttle.CanFTL(ship, out var reason), Is.False, "A test ship can't FTL.");
                Assert.That(reason, Is.EqualTo(server.ResolveDependency<ILocalizationManager>().GetString("wf-mapping-test-no-ftl")));
                Assert.That(outward.Cancelled, "Radio doesn't leave the test.");
                Assert.That(inward.Cancelled, "Radio doesn't reach into the test.");
                Assert.That(within.Cancelled, Is.False, "Radio works inside the test.");
            });

            // The character carries no persistent items or components, which spawning would take off the real one.
            var stored = HumanoidCharacterProfile.DefaultWithSpecies().WithPersistentData(new[] { "flag" },
                new[] { new PersistentProfileComponent("component", false) },
                new[] { new PersistentProfileItem("item", false) });
            var forTest = WFMappingTestSystem.TestProfile(stored);
            Assert.Multiple(() =>
            {
                Assert.That(forTest.Items, Is.Empty);
                Assert.That(forTest.Components, Is.Empty);
                Assert.That(forTest.Flags, Is.EqualTo(new[] { "flag" }));
                Assert.That(stored.Items, Has.Count.EqualTo(1), "The stored profile keeps its items.");
            });

            // No edit of any kind while testing.
            var ownCoords = entMan.GetNetCoordinates(new EntityCoordinates(ownGrid, 0.5f, 0.5f));
            var canUse = new WFMappingCanUseEvent(a, request: true);
            entMan.EventBus.RaiseEvent(EventSource.Local, canUse);
            Assert.Multiple(() =>
            {
                Assert.That(mapping.CanEdit(a, session.RootMap), Is.False);
                Assert.That(mapping.CanMap(a), Is.False);
                Assert.That(canUse.Allowed, Is.False, "Tools requests are refused.");
                Assert.That(mapping.AllowPlacement(a, new MsgPlacement
                {
                    PlaceType = PlacementManagerMessage.RequestPlacement,
                    EntityTemplateName = "WallSolid",
                    NetCoordinates = ownCoords,
                    Align = "None",
                }), Is.False, "Engine placement is refused.");
            });

            // A tester's own save is refused; the autosave still runs and holds only the session's own level.
            RaiseAs(entMan, a, new WFMappingSaveRequest(string.Empty));
            Assert.That(Versions(res, a.UserId, MapName), Is.Empty, "A save request from a tester writes nothing.");
            Assert.That(library.Autosave(session), Is.True);
            var versions = Versions(res, a.UserId, MapName);
            Assert.That(versions, Has.Count.EqualTo(1));
            var folder = WFMappingLibraryNames.VersionFolder(a.UserId, MapName, versions[0]);
            var levelFiles = res.UserData.DirectoryEntries(folder)
                .Where(f => System.Text.RegularExpressions.Regex.IsMatch(f, @"^z-?\d+\.yml$"))
                .ToList();
            Assert.That(levelFiles, Is.EquivalentTo(new[] { "z0.yml" }), "One level file: the session's own.");
            Assert.That(mapSys.IsInitialized(session.RootMap), Is.False, "Saving didn't initialise anything.");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(a.AttachedEntity, Is.EqualTo(mob), "Still testing after a few ticks.");
            RaiseAs(entMan, a, new WFMappingTestStopRequest(false));
            Assert.Multiple(() =>
            {
                Assert.That(a.AttachedEntity, Is.EqualTo(observer), "Back on the same observer.");
                Assert.That(entMan.GetComponent<TransformComponent>(observer).MapUid, Is.EqualTo(session.RootMap));
                Assert.That(session.Test, Is.Null);
                Assert.That(mapping.IsTesting(a), Is.False);
                Assert.That(session.Present[a.UserId], Is.EqualTo(observer));
                Assert.That(mapping.CanEdit(a, session.RootMap), Is.True, "Editing is back.");
            });
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.Deleted(mob), "The test character is gone.");
                foreach (var map in testMaps)
                {
                    Assert.That(entMan.Deleted(map), $"Test map {map} should be deleted.");
                }

                Assert.That(mapping.TestMaps(session.Id), Is.Empty);
                Assert.That(entMan.Deleted(session.RootMap), Is.False);
                Assert.That(Grids(entMan, session.RootMap), Is.EqualTo(1), "The live grid is untouched.");
            });

            mapping.CloseSession(session, autosave: false);
        });

        await pair.RunTicksSync(5);
        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClosingTheSessionEndsTheTest()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var testing = entMan.System<WFMappingTestSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var a = (await AddMappers(pair, 1))[0];

        List<EntityUid> testMaps = new();
        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            // No range this time: the empty-string cvar means space only.
            server.CfgMan.SetCVar(WFMappingModeCVars.TestRange, string.Empty);
            Assert.That(mapping.TryCreateSession(a, MapName, out var created), Is.True);
            var session = created!;
            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            mapSys.SetTile(grid, grid.Comp, new Vector2i(0, 0), new Tile(tileDefs["Plating"].TileId));

            Assert.That(testing.TryStart(a), Is.True);
            mob = a.AttachedEntity!.Value;
            testMaps = session.Test!.Maps.ToList();
            Assert.That(Grids(entMan, session.Test.Ground), Is.EqualTo(1), "Only the ship copy, no range.");

            mapping.CloseSession(session, autosave: false);
            Assert.That(a.AttachedEntity, Is.Null, "Closing the session sends the tester to the lobby.");
            Assert.That(mapping.IsTesting(a), Is.False);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(mob));
            foreach (var map in testMaps)
            {
                Assert.That(entMan.Deleted(map), $"Test map {map} should be deleted.");
            }
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The real client goes from the mapping observer to the test character and back: the mapping screen must leave
    /// and return without the client reloading its screen inside the attach events, which double-loads the hands UI
    /// and trips its assert.
    /// </summary>
    [Test]
    public async Task ClientSwitchesScreens()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var testing = entMan.System<WFMappingTestSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        var uiMan = client.ResolveDependency<IUserInterfaceManager>();
        await Enable(pair);
        var player = pair.Player!;

        await client.WaitPost(() => clientSys.Send(new WFMappingCreateRequest(MapName)));
        for (var i = 0; i < 100 && uiMan.ActiveScreen is not WFMappingScreen; i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(5);
        Assert.That(uiMan.ActiveScreen, Is.TypeOf<WFMappingScreen>(), "The client sits on the mapping screen.");

        WFMappingSession session = default!;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WFMappingModeCVars.TestCooldown, 0f);
            Assert.That(mapping.TryGetPresent(player, out var present), Is.True);
            session = present!;
            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
            mapSys.SetTile(grid, grid.Comp, new Vector2i(0, 0), new Tile(tileDefs["Plating"].TileId));
        });

        // Started and stopped from the client as a player would, a second apart: Back to mapping isn't throttled.
        await pair.RunTicksSync(5);
        await client.WaitPost(() => clientSys.Send(new WFMappingTestStartRequest()));
        await pair.RunTicksSync(30);
        await server.WaitAssertion(() => Assert.That(session.Test, Is.Not.Null, "The test should start."));

        await client.WaitAssertion(() =>
        {
            var local = client.AttachedEntity;
            Assert.That(local, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(client.EntMan.HasComponent<WFMappingTesterComponent>(local!.Value), "The client is on the tester.");
                Assert.That(clientSys.SetUp, Is.False);
                Assert.That(uiMan.ActiveScreen, Is.Not.TypeOf<WFMappingScreen>(), "The gameplay screen is up for the test.");
            });
        });

        await server.WaitAssertion(() => Assert.That(mapping.IsTesting(player), Is.True));
        await client.WaitPost(() => clientSys.Send(new WFMappingTestStopRequest(false)));
        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            var local = client.AttachedEntity;
            Assert.That(local, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(client.EntMan.HasComponent<WFMappingObserverComponent>(local!.Value), "Back on the observer.");
                Assert.That(clientSys.SetUp, Is.True);
                Assert.That(uiMan.ActiveScreen, Is.TypeOf<WFMappingScreen>(), "The mapping screen is back.");
            });
        });

        await server.WaitPost(() => mapping.CloseSession(session, autosave: false));
        await pair.RunTicksSync(5);
        await Disable(pair);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Server-wide limits: no more than <c>test_max_running</c> tests at once, and <c>test_global_cooldown</c> after
    /// any test starts before another is built. A session's own cooldown doesn't stand in for either.
    /// </summary>
    [Test]
    public async Task GlobalCapAndCooldownRefuseBuilds()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var testing = entMan.System<WFMappingTestSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var mappers = await AddMappers(pair, 2);
        var a = mappers[0];
        var b = mappers[1];

        WFMappingSession sessionA = default!;
        WFMappingSession sessionB = default!;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WFMappingModeCVars.TestRange, string.Empty);
            server.CfgMan.SetCVar(WFMappingModeCVars.TestCooldown, 0f);
            server.CfgMan.SetCVar(WFMappingModeCVars.TestGlobalCooldown, 0f);
            server.CfgMan.SetCVar(WFMappingModeCVars.TestMaxRunning, 1);
            Assert.That(mapping.TryCreateSession(a, MapName + " A", out var createdA), Is.True);
            Assert.That(mapping.TryCreateSession(b, MapName + " B", out var createdB), Is.True);
            sessionA = createdA!;
            sessionB = createdB!;
            foreach (var session in new[] { sessionA, sessionB })
            {
                var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session.RootMap).MapId);
                mapSys.SetTile(grid, grid.Comp, new Vector2i(0, 0), new Tile(tileDefs["Plating"].TileId));
            }

            // One test may run: the second session's is refused by the cap.
            Assert.That(testing.TryStart(a), Is.True);
            Assert.That(testing.TryStart(b), Is.False, "The cap refuses a second running test.");
            Assert.That(sessionB.Test, Is.Null);
            Assert.That(mapping.IsTesting(b), Is.False);

            // Room for more, but a build starts the server-wide cooldown: the next build waits on it.
            server.CfgMan.SetCVar(WFMappingModeCVars.TestMaxRunning, 8);
            server.CfgMan.SetCVar(WFMappingModeCVars.TestGlobalCooldown, 100f);
            Assert.That(testing.TryStart(b), Is.True, "Under the cap and past the cooldown, the test starts.");
            testing.EndTest(a.UserId);
            Assert.That(sessionA.Test, Is.Null);
            Assert.That(testing.TryStart(a), Is.False, "The global cooldown refuses the next build.");
            Assert.That(sessionA.Test, Is.Null);
            Assert.That(mapping.IsTesting(a), Is.False);

            mapping.CloseSession(sessionA, autosave: false);
            mapping.CloseSession(sessionB, autosave: false);
        });

        await pair.RunTicksSync(5);
        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
