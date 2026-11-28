#nullable enable
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.Cargo.Systems;
using Content.Shared.Cargo.Events;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// A grid with <see cref="WGRefusesNarcoticsComponent"/>, the Underground Black Market, leaves narcotics on its sale
/// pallet, loose or packed in a crate, and still buys the rest; any other pallet buys them.
/// </summary>
[TestFixture]
[TestOf(typeof(CargoSystem))]
public sealed class NarcoticRefusalTest
{
    [TestCase(true, TestName = "RefusingGridLeavesNarcotics")]
    [TestCase(false, TestName = "OtherGridBuysNarcotics")]
    public async Task PalletRefusesNarcotics(bool refuses)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var storage = entMan.System<SharedEntityStorageSystem>();

        await server.WaitAssertion(() =>
        {
            if (refuses)
                entMan.AddComponent<WGRefusesNarcoticsComponent>(map.Grid.Owner);

            var pad = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
            var pallet = entMan.SpawnEntity("CargoPalletSell", pad);
            var xform = entMan.GetComponent<TransformComponent>(pallet);
            if (!xform.Anchored)
                entMan.System<SharedTransformSystem>().AnchorEntity((pallet, xform));
            var console = entMan.SpawnEntity("ComputerPalletConsole", pad);

            var loose = entMan.SpawnEntity("WGBaggieCocaine", pad);
            var packed = entMan.SpawnEntity("CrateGenericSteel", pad);
            Assert.That(storage.Insert(entMan.SpawnEntity("WGBaggieCocaine", pad), packed), "The crate should take the baggie.");
            var clean = entMan.SpawnEntity("CrateGenericSteel", pad);

            entMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());

            Assert.Multiple(() =>
            {
                Assert.That(entMan.EntityExists(loose), Is.EqualTo(refuses), "A loose baggie was handled wrong.");
                Assert.That(entMan.EntityExists(packed), Is.EqualTo(refuses), "A crate holding a baggie was handled wrong.");
                Assert.That(entMan.EntityExists(clean), Is.False, "Other goods on the pallet must still sell.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
