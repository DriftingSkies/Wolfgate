#nullable enable
using Content.Server._WG.Xenonids.Hive;
using Content.Shared._WG.Xenonids.Hive;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Xenonids;

/// <summary>A xeno spawned with no hive gets the default hive, which is created on demand.</summary>
[TestFixture]
public sealed class XenoHiveBootstrapTest
{
    [Test]
    public async Task SpawnedXenoGetsDefaultHive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var hives = entities.System<SharedXenoHiveSystem>();
        var bootstrap = entities.System<WGXenoHiveBootstrapSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var larva = entities.SpawnEntity("CMXenoLarva", map.GridCoords);
            var hive = hives.GetHive(larva);
            Assert.That(hive, Is.Not.Null, "A hiveless xeno is put in the default hive.");
            Assert.That(entities.GetComponent<MetaDataComponent>(hive!.Value).EntityName, Is.EqualTo(WGXenoHiveBootstrapSystem.DefaultHiveName));

            var drone = entities.SpawnEntity("CMXenoLarva", map.GridCoords);
            Assert.That(hives.GetHive(drone), Is.EqualTo(hive), "The second xeno joins the same hive.");

            hives.ChangeBurrowedLarva(bootstrap.EnsureDefaultHive(), 3);
            Assert.That(hive.Value.Comp.BurrowedLarva, Is.EqualTo(3));
        });

        await pair.CleanReturnAsync();
    }
}
