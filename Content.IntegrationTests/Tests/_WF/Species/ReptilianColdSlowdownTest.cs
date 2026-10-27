#nullable enable
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Shared.Temperature.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>
/// A lizard back from the freezer stayed at the cold slowdown: warming past a threshold applied that threshold's own
/// heavier slowdown. In room air it settles near 295 K, where it walks as a lizard always does there.
/// </summary>
[TestFixture]
public sealed class ReptilianColdSlowdownTest : WolfmedGameTest
{
    [Test]
    public async Task WarmedLizardLosesTheColdSlowdownTest()
    {
        var map = await CreateTestMap();
        EntityUid warm = default, cold = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            warm = SEntMan.SpawnEntity("MobReptilian", map.GridCoords);
            cold = SEntMan.SpawnEntity("MobReptilian", map.GridCoords);
        });
        await RunSeconds(1);
        await Server.WaitAssertion(() => SEntMan.GetComponent<TemperatureComponent>(cold).CurrentTemperature = 270f);
        await RunSeconds(180);

        await Server.WaitAssertion(() =>
        {
            var settled = SEntMan.GetComponent<TemperatureSpeedComponent>(warm).CurrentSpeedModifier;
            Assert.That(SEntMan.GetComponent<TemperatureSpeedComponent>(cold).CurrentSpeedModifier, Is.EqualTo(settled),
                "the lizard warmed out of the cold walks as one that never left the room.");
        });
    }
}
