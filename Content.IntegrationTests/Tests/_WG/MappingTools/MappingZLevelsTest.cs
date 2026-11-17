#nullable enable
using System.Linq;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WG.MappingTools;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.MappingTools;
using Robust.Server.GameObjects;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using ClientZLevels = Content.Shared._CE.ZLevels.Core.EntitySystems.CESharedZLevelsSystem;

namespace Content.IntegrationTests.Tests._WG.MappingTools;

/// <summary>
/// Mapping z-stacks grow and shrink around a fixed ground level, save one file per level and reach the client.
/// </summary>
[TestFixture]
public sealed class MappingZLevelsTest
{
    private const string StackName = "wg_test_stack";

    [Test]
    public async Task AddRemoveAndSave()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var stacks = entMan.System<MappingZLevelsSystem>();
        var zLevels = entMan.System<CEZLevelsSystem>();
        var mapSys = entMan.System<MapSystem>();
        var res = server.ResolveDependency<IResourceManager>();

        EntityUid ground = default;
        EntityUid networkUid = default;

        await server.WaitAssertion(() =>
        {
            ground = mapSys.CreateMap(out _, runMapInit: false);

            // A lone map becomes level 0 of a new stack, with the new level above it.
            Assert.That(stacks.TryAddLevel(ground, true, out var upper, out var upperDepth, out _), Is.True);
            Assert.That(upperDepth, Is.EqualTo(1));
            Assert.That(Depths(ground), Is.EqualTo(new[] { 0, 1 }));

            Assert.That(stacks.TryAddLevel(ground, false, out var basement, out var basementDepth, out _), Is.True);
            Assert.That(basementDepth, Is.EqualTo(-1));

            // Inserting between the basement and the ground pushes the basement down, never the ground.
            Assert.That(stacks.TryAddLevel(basement, true, out var between, out var betweenDepth, out _), Is.True);
            Assert.That(betweenDepth, Is.EqualTo(-1));
            Assert.That(Depth(basement), Is.EqualTo(-2));
            Assert.That(Depth(ground), Is.EqualTo(0));
            Assert.That(Depth(upper), Is.EqualTo(1));
            Assert.That(zLevels.TryMapOffset(between, 1, out var aboveBetween), Is.True);
            Assert.That(aboveBetween.Owner, Is.EqualTo(ground));

            Assert.That(stacks.TrySaveStack(stacks.GetLevels(ground, out var groundIndex), groundIndex, StackName), Is.True);
            for (var depth = -2; depth <= 1; depth++)
            {
                Assert.That(res.UserData.Exists(new ResPath(MappingZLevels.StackPath(StackName, depth))), Is.True,
                    $"Level {depth} was saved.");
            }

            var files = stacks.FindStackFiles(new ResPath(MappingZLevels.StackPath(StackName, 1)));
            Assert.That(files.Keys, Is.EqualTo(new[] { -2, -1, 0, 1 }));

            // Removing the ground hands depth 0 to the level above it.
            Assert.That(stacks.TryRemoveLevel(ground, out _), Is.True);
            Assert.That(Depth(upper), Is.EqualTo(0));
            Assert.That(Depth(between), Is.EqualTo(-1));
            Assert.That(Depth(basement), Is.EqualTo(-2));

            // Saving again drops the level file the stack no longer has.
            Assert.That(stacks.TrySaveStack(stacks.GetLevels(upper, out var upperIndex), upperIndex, StackName), Is.True);
            Assert.That(res.UserData.Exists(new ResPath(MappingZLevels.StackPath(StackName, 1))), Is.False);
            Assert.That(res.UserData.Exists(new ResPath(MappingZLevels.StackPath(StackName, -2))), Is.True);

            Assert.That(zLevels.TryGetMapNetwork(upper, out var network), Is.True);
            networkUid = network.Owner;

            // A live map is left alone.
            var live = mapSys.CreateMap(out _);
            Assert.That(stacks.TryAddLevel(live, true, out _, out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo("wg-mapping-zlevels-live"));
            Assert.That(stacks.TryRemoveLevel(upper, out _), Is.True);
            Assert.That(stacks.TryRemoveLevel(between, out _), Is.True);
            Assert.That(stacks.TryRemoveLevel(basement, out error), Is.False);
            Assert.That(error, Is.EqualTo("wg-mapping-zlevels-last"));
            Assert.That(zLevels.TryGetMapNetwork(basement, out network), Is.True);
            networkUid = network.Owner;
            entMan.DeleteEntity(live);
        });

        await pair.RunTicksSync(10);

        // The client sees the rebuilt stack.
        await client.WaitAssertion(() =>
        {
            var clientNetwork = client.EntMan.GetEntity(server.EntMan.GetNetEntity(networkUid));
            Assert.That(client.EntMan.TryGetComponent<CEZMapNetworkComponent>(clientNetwork, out var comp), Is.True);
            Assert.That(comp!.ZLevels.Keys, Is.EquivalentTo(new[] { 0 }));
            Assert.That(client.EntMan.System<ClientZLevels>(), Is.Not.Null);
        });

        await server.WaitPost(() =>
        {
            foreach (var entry in res.UserData.DirectoryEntries(new ResPath($"{MappingMaps.SaveFolder}/{StackName}")).ToList())
            {
                res.UserData.Delete(new ResPath($"{MappingMaps.SaveFolder}/{StackName}/{entry}"));
            }

            zLevels.DeleteMapNetwork(networkUid);
        });

        await pair.CleanReturnAsync();
        return;

        int Depth(EntityUid map) => entMan.GetComponent<CEZMapComponent>(map).Depth;

        int[] Depths(EntityUid map) => stacks.GetLevels(map, out _).Select(Depth).ToArray();
    }
}
