using System.Linq;
using Content.Server._WG.Performance;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks a grid parsed off the main thread loads the same as one loaded straight from its file.</summary>
[TestFixture]
[TestOf(typeof(WGGridFileReader))]
public sealed class WGGridFileReaderTest
{
    private static readonly ResPath Hull = new("/SharedMaps/_WG/Shipyard/Shuttles/Mieyo/dredger.yml");

    [Test]
    public async Task ParsedLoadMatchesTryLoadGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var loader = server.System<MapLoaderSystem>();
        var resources = server.ResolveDependency<IResourceManager>();
        var direct = await pair.CreateTestMap();
        var parsed = await pair.CreateTestMap();

        var data = await WGGridFileReader.ReadAsync(resources, Hull);
        Assert.That(data, Is.Not.Null, "The hull file should read as one document.");

        await server.WaitAssertion(() =>
        {
            Assert.That(loader.TryLoadGrid(direct.MapId, Hull, out var straight), Is.True);
            Assert.That(WGGridFileReader.TryLoad(loader, data!, Hull, parsed.MapId, out var fromData), Is.True);

            Assert.That(Count(fromData!.Value.Owner), Is.EqualTo(Count(straight!.Value.Owner)),
                "Both loads should put the same number of entities on the hull.");
            Assert.That(entMan.GetComponent<MapGridComponent>(fromData.Value.Owner).LocalAABB,
                Is.EqualTo(entMan.GetComponent<MapGridComponent>(straight.Value.Owner).LocalAABB));

            entMan.DeleteEntity(straight.Value.Owner);
            entMan.DeleteEntity(fromData.Value.Owner);
        });

        await pair.CleanReturnAsync();

        int Count(EntityUid grid)
        {
            var count = 0;
            var children = entMan.GetComponent<TransformComponent>(grid).ChildEnumerator;
            while (children.MoveNext(out _))
            {
                count++;
            }

            return count;
        }
    }

    [Test]
    public async Task MissingFileReadsAsNull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var resources = pair.Server.ResolveDependency<IResourceManager>();

        Assert.That(await WGGridFileReader.ReadAsync(resources, new ResPath("/SharedMaps/_WG/none.yml")), Is.Null);

        await pair.CleanReturnAsync();
    }
}
