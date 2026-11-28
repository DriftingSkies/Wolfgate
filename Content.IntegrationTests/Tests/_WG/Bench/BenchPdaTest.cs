#nullable enable
using System.Linq;
using Content.Server._WG.Bench;
using Content.Server.CartridgeLoader;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Bench;

/// <summary>
/// The judge's and the Overseer's PDAs come with the Bench app, so any copy of one, a reprint included, carries it.
/// </summary>
[TestFixture]
[TestOf(typeof(WGBenchSystem))]
public sealed class BenchPdaTest
{
    [TestCase("SecurityGuardPDA")]
    [TestCase("SrPDA")]
    public async Task JudgePdaHasBench(string pdaProto)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var entMan = server.EntMan;
        // On a map, so map init installs the preinstalled programs.
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var pda = entMan.SpawnEntity(pdaProto, map.GridCoords);
            var installed = entMan.System<CartridgeLoaderSystem>().GetInstalled(pda)
                .Select(program => entMan.GetComponent<MetaDataComponent>(program).EntityPrototype?.ID);

            Assert.That(installed, Does.Contain(WGBenchSystem.Cartridge.Id), $"{pdaProto} has no Bench app.");
            entMan.DeleteEntity(pda);
        });

        await pair.CleanReturnAsync();
    }
}
