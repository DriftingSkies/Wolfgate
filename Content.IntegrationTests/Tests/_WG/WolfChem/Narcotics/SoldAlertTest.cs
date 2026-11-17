#nullable enable
using Content.Server.Cargo.Systems;
using Content.Shared.Cargo.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>Selling drugs off a cargo pallet radios Security.</summary>
[TestFixture]
[TestOf(typeof(CargoSystem))]
public sealed class SoldAlertTest
{
    [Test]
    public async Task SellingDrugsAlertsSecurity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var probe = server.System<RadioProbeSystem>();
            var pad = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
            var pallet = entMan.SpawnEntity("CargoPalletSell", pad);
            var xform = entMan.GetComponent<TransformComponent>(pallet);
            if (!xform.Anchored)
                server.System<SharedTransformSystem>().AnchorEntity((pallet, xform));

            var console = entMan.SpawnEntity("ComputerPalletConsole", pad);
            var rock = entMan.SpawnEntity("WGCrackRock", pad);
            var baggie = entMan.SpawnEntity("WGBaggieHeroin", pad);
            probe.Sent.Clear();

            entMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());

            Assert.Multiple(() =>
            {
                Assert.That(entMan.EntityExists(rock) || entMan.EntityExists(baggie), Is.False, "The drugs should have sold.");
                Assert.That(probe.Sent, Has.Some.Matches<(string Channel, EntityUid Source)>(s => s.Channel == "Security"),
                    "The sale should be radioed to Security.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
