using System.Numerics;
using Content.Server._Mono.Temperature.Systems;
using Content.Server.Temperature.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks a spread-out heater still warms what's loose near it, once a second, and nothing out of range.</summary>
[TestFixture]
[TestOf(typeof(EntityRadiusHeaterSystem))]
public sealed class WGRadiusHeaterTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGRadiusHeaterDummy
  components:
  - type: EntityRadiusHeater
    radius: 3
    thermalEnergy: 100
    limit: 100000000

- type: entity
  id: WGRadiusHeaterTarget
  components:
  - type: Temperature
    currentTemperature: 200
    coldDamageThreshold: 0
    heatDamageThreshold: 1000
";

    [Test]
    public async Task WarmsNearbyOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid near = default;
        EntityUid far = default;
        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("WGRadiusHeaterDummy", new MapCoordinates(new Vector2(500f, 500f), map.MapId));
            near = entMan.SpawnEntity("WGRadiusHeaterTarget", new MapCoordinates(new Vector2(501f, 500f), map.MapId));
            far = entMan.SpawnEntity("WGRadiusHeaterTarget", new MapCoordinates(new Vector2(520f, 500f), map.MapId));
        });

        float Temp(EntityUid uid) => entMan.GetComponent<TemperatureComponent>(uid).CurrentTemperature;

        await pair.RunTicksSync(35);
        float afterOne = 0;
        await server.WaitAssertion(() =>
        {
            afterOne = Temp(near);
            Assert.That(afterOne, Is.GreaterThan(200f), "A loose thing in range should warm within a second.");
            Assert.That(Temp(far), Is.EqualTo(200f), "A thing out of range should not.");
        });

        await pair.RunTicksSync(30);
        await server.WaitAssertion(() => Assert.That(Temp(near), Is.GreaterThan(afterOne), "It should keep warming each second."));

        await pair.CleanReturnAsync();
    }
}
