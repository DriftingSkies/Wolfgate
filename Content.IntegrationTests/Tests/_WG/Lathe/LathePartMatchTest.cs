using Content.Shared._WG.Lathe;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Lathe;

/// <summary>Lathe recipe parts accept variants and other stacks of the part, as crafted and printed items are.</summary>
[TestFixture]
public sealed class LathePartMatchTest
{
    [Test]
    public async Task VariantsAndStacksCountAsParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var silo = entities.System<SharedFabricationSiloSystem>();

        await server.WaitAssertion(() =>
        {
            var screw = entities.SpawnEntity("MaterialScrewSingle", MapCoordinates.Nullspace);
            var crowbar = entities.SpawnEntity("CrowbarGreen", MapCoordinates.Nullspace);
            var wrench = entities.SpawnEntity("Wrench", MapCoordinates.Nullspace);
            var emergency = entities.SpawnEntity("CrowbarRed", MapCoordinates.Nullspace);

            Assert.Multiple(() =>
            {
                Assert.That(silo.IsRecipePart(screw, "MaterialScrew"), "A crafted screw is a screw.");
                Assert.That(silo.IsRecipePart(crowbar, "Crowbar"), "A printed crowbar is a crowbar.");
                Assert.That(silo.IsRecipePart(wrench, "Wrench"), "A part is itself.");
                Assert.That(silo.IsRecipePart(wrench, "Crowbar"), Is.False, "A wrench is no crowbar.");
                Assert.That(silo.IsRecipePart(emergency, "Crowbar"), Is.False, "A differently named crowbar is another item.");
            });

            entities.DeleteEntity(screw);
            entities.DeleteEntity(crowbar);
            entities.DeleteEntity(wrench);
            entities.DeleteEntity(emergency);
        });

        await pair.CleanReturnAsync();
    }
}
