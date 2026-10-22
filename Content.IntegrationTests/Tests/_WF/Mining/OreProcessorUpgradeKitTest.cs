using Content.Shared._DV.Construction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Mining;

/// <summary>Only the hyper-convection kit fits an ore processor; every kit still fits a lathe.</summary>
[TestFixture]
public sealed class OreProcessorUpgradeKitTest
{
    private const string OreProcessor = "OreProcessor";
    private const string Autolathe = "Autolathe";
    private const string HyperKit = "LatheUpgradeKitHyper";
    private const string CryoKit = "LatheUpgradeKitCryo";

    [Test]
    public async Task HyperKitFitsOreProcessor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var kits = server.System<UpgradeKitSystem>();
            var coords = map.MapCoords;
            var processor = em.SpawnEntity(OreProcessor, coords);
            var lathe = em.SpawnEntity(Autolathe, coords);
            var user = em.SpawnEntity("MobHuman", coords);
            var hyper = em.SpawnEntity(HyperKit, coords);
            var cryo = em.SpawnEntity(CryoKit, coords);
            var hyperKit = new Entity<UpgradeKitComponent>(hyper, em.GetComponent<UpgradeKitComponent>(hyper));
            var cryoKit = new Entity<UpgradeKitComponent>(cryo, em.GetComponent<UpgradeKitComponent>(cryo));

            Assert.Multiple(() =>
            {
                Assert.That(kits.CanUpgrade(hyperKit, processor, user), Is.True, "hyper-convection kit fits an ore processor");
                Assert.That(kits.CanUpgrade(cryoKit, processor, user), Is.False, "cryogenic kit doesn't fit an ore processor");
                Assert.That(kits.CanUpgrade(hyperKit, lathe, user), Is.True, "hyper-convection kit fits a lathe");
                Assert.That(kits.CanUpgrade(cryoKit, lathe, user), Is.True, "cryogenic kit fits a lathe");
            });
        });

        await pair.CleanReturnAsync();
    }
}
