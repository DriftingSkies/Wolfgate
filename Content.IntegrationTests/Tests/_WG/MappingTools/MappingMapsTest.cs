#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client._WG.ShipPreview;
using Content.Shared._WG.MappingTools;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using ServerMaps = Content.Server._WG.MappingTools.MappingMapsSystem;

namespace Content.IntegrationTests.Tests._WG.MappingTools;

/// <summary>
/// The Maps window's server side saves into its folder, lists what it can open and hands the client text the
/// previewer can load.
/// </summary>
[TestFixture]
public sealed class MappingMapsTest
{
    [Test]
    public async Task SaveListAndPreview()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var testMap = await pair.CreateTestMap();
        var maps = server.EntMan.System<ServerMaps>();

        MappingMapsPreviewEvent? preview = null;
        string? shipPath = null;
        await server.WaitAssertion(() =>
        {
            var grid = testMap.Grid;
            server.EntMan.SpawnEntity("WallSolid", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));

            Assert.That(maps.TrySave(grid, "../escape", false, out _), Is.False, "Names can't leave the save folder.");
            Assert.That(maps.TrySave(grid, "wg_test_save", false, out var path), Is.True);
            Assert.That(path.ToString(), Is.EqualTo($"{MappingMaps.SaveFolder}/wg_test_save.yml"));

            var files = maps.ListFiles();
            Assert.That(files, Does.Contain(new MappingMapFile(path.ToString(), MappingMapSource.Saved)));

            // A game ship is listed and readable too.
            var ship = files.First(f => f.Source == MappingMapSource.Ships);
            shipPath = ship.Path;
            var shipPreview = maps.ReadForPreview(ship.Path);
            Assert.That(shipPreview.Yaml != null || shipPreview.Error == "wg-mapping-maps-preview-too-large", Is.True);

            // Anything not on the list stays out of reach.
            Assert.That(maps.ReadForPreview("/Prototypes/Entities/Structures/Walls/walls.yml").Yaml, Is.Null);
            Assert.That(maps.ReadForPreview("/Mapping/../server_config.toml").Yaml, Is.Null);

            preview = maps.ReadForPreview(path.ToString());
            Assert.That(preview.Yaml, Is.Not.Null);
        });

        await client.WaitAssertion(() =>
        {
            var previews = client.EntMan.System<ShipPreviewSystem>();
            var handle = previews.Acquire();
            try
            {
                Assert.That(previews.TryLoadText(handle, new ResPath(preview!.Path), preview.Yaml!, null, out var loaded), Is.True);
                Assert.That(loaded.TileCount, Is.GreaterThan(0));

                // The shipyard's previewer reads game ship files straight off disk the same way.
                Assert.That(previews.TryLoad(handle, new ResPath(shipPath!), null, out var ship), Is.True);
                Assert.That(ship.TileCount, Is.GreaterThan(0));
            }
            finally
            {
                previews.Release(handle);
            }
        });

        await pair.CleanReturnAsync();
    }
}
