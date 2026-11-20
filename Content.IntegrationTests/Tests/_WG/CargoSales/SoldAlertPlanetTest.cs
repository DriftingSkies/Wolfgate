#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests._WG.PlanetComms;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._NF.SectorServices;
using Content.Server._WG.PlanetComms;
using Content.Server.Cargo.Systems;
using Content.Shared.Cargo.Events;
using Content.Shared.Radio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.CargoSales;

/// <summary>A sold alert from a pallet on a world whose comms are down still reaches off-world radios.</summary>
[TestFixture]
[TestOf(typeof(CargoSystem))]
public sealed class SoldAlertPlanetTest
{
    /// <summary>A PDV supply crate, which alerts Command when sold.</summary>
    private const string Crate = "CrateTradeContrabandSecure1";

    [Test]
    public async Task AlertLeavesADarkWorld()
    {
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var entMan = server.EntMan;
        var spy = server.System<WGPlanetCommsTestSystem>();

        var layers = await PlanetFixture.BuildStandalone(pair);
        var ground = layers[0];
        await PlanetFixture.LayTiles(pair, ground, new Vector2i(-2, -2), new Vector2i(4, 4));
        var offworld = await pair.CreateTestMap();

        try
        {
            EntityUid listener = default, console = default, crate = default;
            await server.WaitPost(() =>
            {
                if (!entMan.EntityExists(server.System<SectorServiceSystem>().GetServiceEntity()))
                    entMan.AddComponent<StationSectorServiceHostComponent>(offworld.MapUid);

                listener = entMan.SpawnEntity(null, offworld.GridCoords);
                entMan.EnsureComponent<ActiveRadioComponent>(listener).Channels.Add("Command");
                entMan.EnsureComponent<WGPlanetCommsTestReceiverComponent>(listener);

                var pad = new EntityCoordinates(ground, new Vector2(0.5f, 0.5f));
                var pallet = entMan.SpawnEntity("CargoPalletSell", pad);
                var xform = entMan.GetComponent<TransformComponent>(pallet);
                if (!xform.Anchored)
                    server.System<SharedTransformSystem>().AnchorEntity((pallet, xform));

                console = entMan.SpawnEntity("ComputerPalletConsole", pad);
                crate = entMan.SpawnEntity(Crate, pad);
            });
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WGPlanetCommsSystem>().IsCommsUp(ground), Is.False, "The world should start with its comms down.");
                spy.Deliveries.Clear();
                entMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
            });
            await server.WaitRunTicks(1);

            Assert.That(entMan.Deleted(crate), Is.True, "The crate should have sold.");
            Assert.That(spy.Deliveries, Does.Contain(listener), "A sale on a world with its comms down never alerted Command.");
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
