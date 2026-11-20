#nullable enable
using System.Numerics;
using Content.IntegrationTests.Tests._WG.PlanetComms;
using Content.IntegrationTests.Tests._WG.Planets;
using Content.Server._WG.PlanetComms;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Shared.Radio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>A sale on a world whose comms are down still tips off security off-world.</summary>
[TestFixture]
[TestOf(typeof(WGNarcoticHeatSystem))]
public sealed class NarcoticTipPlanetTest
{
    [Test]
    public async Task TipLeavesADarkWorld()
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
            EntityUid listener = default;
            await server.WaitPost(() =>
            {
                listener = entMan.SpawnEntity(null, offworld.GridCoords);
                entMan.EnsureComponent<ActiveRadioComponent>(listener).Channels.Add("Security");
                entMan.EnsureComponent<WGPlanetCommsTestReceiverComponent>(listener);
            });
            await server.WaitRunTicks(2);

            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WGPlanetCommsSystem>().IsCommsUp(ground), Is.False, "The world should start with its comms down.");
                var pad = new EntityCoordinates(ground, new Vector2(0.5f, 0.5f));
                var seller = entMan.SpawnEntity("MobHuman", pad);
                var console = entMan.SpawnEntity("ComputerPalletConsole", pad);
                spy.Deliveries.Clear();
                server.System<WGNarcoticHeatSystem>().Sold(seller, console, ground, 100);
            });
            await server.WaitRunTicks(1);

            Assert.That(spy.Deliveries, Does.Contain(listener), "A sale on a world with its comms down never reached security.");
        }
        finally
        {
            await PlanetFixture.Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
