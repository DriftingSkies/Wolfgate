using Content.Server.IgnitionSource;
using Content.Shared.IgnitionSource;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks the lit list follows an ignition source through spawning, toggling and deletion.</summary>
[TestFixture]
[TestOf(typeof(IgnitionSourceSystem))]
public sealed class WGIgnitionLitTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGIgnitionLitDummy
  components:
  - type: IgnitionSource
    temperature: 700
    ignited: false

- type: entity
  id: WGIgnitionLitBurning
  components:
  - type: IgnitionSource
    temperature: 700
    ignited: true
";

    [Test]
    public async Task ListFollowsSource()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var system = server.System<IgnitionSourceSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var unlit = server.EntMan.SpawnEntity("WGIgnitionLitDummy", map.GridCoords);
            var burning = server.EntMan.SpawnEntity("WGIgnitionLitBurning", map.GridCoords);
            Assert.That(system.WGLit, Does.Not.Contain(unlit), "An unlit source should not be listed.");
            Assert.That(system.WGLit, Does.Contain(burning), "A source spawned lit should be listed.");

            system.SetIgnited(unlit);
            Assert.That(system.WGLit, Does.Contain(unlit));

            system.SetIgnited(unlit, false);
            Assert.That(system.WGLit, Does.Not.Contain(unlit));

            server.EntMan.DeleteEntity(burning);
            Assert.That(system.WGLit, Does.Not.Contain(burning), "A deleted source should leave the list.");
            server.EntMan.DeleteEntity(unlit);
        });

        await pair.CleanReturnAsync();
    }
}
