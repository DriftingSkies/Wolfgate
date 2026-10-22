#nullable enable
using System.IO;
using System.Linq;
using System.Text;
using Content.Client._WF.MappingMode;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// The real client's upload path, from the bytes a picked file gives it to the version in its library, and through
/// the unknown-prototype warning to a confirmed upload. The picker itself can't run headless, so this starts where it
/// hands the file over.
/// </summary>
[TestFixture]
public sealed class WFMappingUploadClientTest
{
    [Test]
    public async Task ClientUploadLandsInLibrary()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var loader = entMan.System<MapLoaderSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        var res = server.ResolveDependency<IResourceManager>();
        var upload = client.EntMan.System<WFMappingUploadClientSystem>();
        await Enable(pair);
        var player = pair.Player!;

        // A small map saved to text, as a file on the player's disk would hold it; then with a wall of a prototype
        // this server lacks.
        const string missingProto = "WFUploadClientTestMissing";
        var yaml = string.Empty;
        var missingYaml = string.Empty;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadCooldown, 0f);
            server.CfgMan.SetCVar(WFMappingModeCVars.ReferenceCooldown, 0f);
            Assert.That(mapping.TryCreateSession(player, "Upload Source", out var source), Is.True);
            var mapId = entMan.GetComponent<MapComponent>(source!.RootMap).MapId;
            var grid = mapSys.CreateGridEntity(mapId);
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, Enumerable.Range(0, 4).Select(x => (new Vector2i(x, 0), plating)).ToList());
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveMap(source.RootMap, writer), Is.True);
            yaml = writer.ToString();

            entMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, 1.5f, 0.5f));
            using var withWall = new StringWriter();
            Assert.That(loader.TrySaveMap(source.RootMap, withWall), Is.True);
            missingYaml = withWall.ToString().Replace("proto: WallSolid", $"proto: {missingProto}");
        });
        await pair.RunTicksSync(5);

        var bytes = Encoding.UTF8.GetBytes(yaml);
        await client.WaitPost(() => upload.Start("Client Upload", bytes));
        for (var i = 0; i < 200 && upload.Upload is not { Stage: WFMappingUploadStage.Done }; i++)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(upload.Upload, Is.Not.Null, "The upload never started on the client.");
            Assert.That(upload.Upload!.Stage, Is.EqualTo(WFMappingUploadStage.Done), "The upload did not finish.");
        });
        await server.WaitAssertion(() =>
        {
            var folder = WFMappingLibraryNames.MapFolder(player.UserId, "Client Upload");
            Assert.That(res.UserData.IsDir(folder), Is.True, "The library has no folder for the upload.");
            var versions = res.UserData.DirectoryEntries(folder).ToList();
            Assert.That(versions, Has.Count.EqualTo(1));
            Assert.That(versions[0], Does.EndWith("_upload"));
        });

        // The unknown prototype stops at a warning; Load anyway stores it without the wall.
        await client.WaitPost(() => upload.Start("Client Missing", Encoding.UTF8.GetBytes(missingYaml)));
        for (var i = 0; i < 200 && upload.Upload is not { Stage: WFMappingUploadStage.Confirming }; i++)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(upload.Upload?.Stage, Is.EqualTo(WFMappingUploadStage.Confirming), "No warning came.");
            var progress = upload.Upload!;
            Assert.Multiple(() =>
            {
                Assert.That(progress.MissingPrototypes, Is.EqualTo(new[] { missingProto }));
                Assert.That(progress.MissingPrototypeCount, Is.EqualTo(1));
                Assert.That(progress.EntitiesToDrop, Is.EqualTo(1));
                Assert.That(WFMappingUploadRow.WarningText(progress),
                    Is.EqualTo($"This server doesn't know 1 prototype used by 1 entity: {missingProto}"));
            });
            upload.ConfirmDropMissing();
        });

        for (var i = 0; i < 200 && upload.Upload is not { Stage: WFMappingUploadStage.Done }; i++)
        {
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            Assert.That(upload.Upload?.Stage, Is.EqualTo(WFMappingUploadStage.Done), "The confirmed upload did not finish.");
            Assert.That(upload.Upload!.EntitiesToDrop, Is.EqualTo(1));
        });
        await server.WaitAssertion(() =>
        {
            var folder = WFMappingLibraryNames.MapFolder(player.UserId, "Client Missing");
            Assert.That(res.UserData.IsDir(folder), Is.True);
            Assert.That(res.UserData.DirectoryEntries(folder).ToList(), Has.Count.EqualTo(1));
            server.CfgMan.SetCVar(WFMappingModeCVars.UploadCooldown, WFMappingModeCVars.UploadCooldown.DefaultValue);
            server.CfgMan.SetCVar(WFMappingModeCVars.ReferenceCooldown, WFMappingModeCVars.ReferenceCooldown.DefaultValue);
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
