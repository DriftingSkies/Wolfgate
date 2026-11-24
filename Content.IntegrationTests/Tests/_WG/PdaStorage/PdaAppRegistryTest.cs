#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Shared._WG.PdaStorage.Prototypes;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.PdaStorage;

/// <summary>
/// Every store listing points at a real program, a real category and a localised description, and
/// sizes follow the 8 GQ step the 128 GQ PDA drive is built around.
/// </summary>
[TestFixture]
public sealed class PdaAppRegistryTest
{
    [Test]
    public async Task CatalogueIsConsistentTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var loc = server.ResolveDependency<ILocalizationManager>();

            var apps = prototypes.EnumeratePrototypes<PdaAppPrototype>().ToList();
            Assert.That(apps, Is.Not.Empty, "the store has listings");
            Assert.That(apps.Count(app => app.AvailableInStore), Is.GreaterThan(0));

            foreach (var app in apps)
            {
                Assert.That(prototypes.HasIndex(app.Category), Is.True,
                    $"{app.ID}: category {app.Category} exists");

                Assert.That(loc.TryGetString(app.Description, out _), Is.True,
                    $"{app.ID}: description {app.Description} is localised");

                Assert.That(prototypes.TryIndex(app.Program, out EntityPrototype? program), Is.True,
                    $"{app.ID}: program {app.Program} exists");

                Assert.That(program!.TryGetComponent<CartridgeComponent>(out var cartridge), Is.True,
                    $"{app.ID}: program {app.Program} is a cartridge program");

                Assert.That(cartridge!.Size, Is.GreaterThan(0).And.MultipleOf(8),
                    $"{app.ID}: size {cartridge.Size} follows the 8 GQ step");

                if (app.AvailableInStore)
                {
                    Assert.That(cartridge.Readonly, Is.False,
                        $"{app.ID}: store apps must be removable so their storage can be refunded");
                }

                Assert.That(app.Devices.HasFlag(PdaDeviceFlags.Pda), Is.True,
                    $"{app.ID}: lists the PDA as a supported device");

                // Every program id in the catalogue must be unique per listing; two listings pointing
                // at the same program would make duplicate checks ambiguous.
                Assert.That(apps.Count(other => other.Program == app.Program), Is.EqualTo(1),
                    $"{app.ID}: only one listing per program prototype");
            }
        });

        await pair.CleanReturnAsync();
    }
}
