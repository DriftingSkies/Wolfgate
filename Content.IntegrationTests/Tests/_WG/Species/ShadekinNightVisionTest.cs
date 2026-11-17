#nullable enable
using Content.Shared._Starlight.Shadekin;
using Content.Shared.Overlays;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Species;

/// <summary>
/// Shadekin night vision is enabled at the Dark light level and disabled again once they are lit.
/// </summary>
[TestFixture]
[TestOf(typeof(ShadekinComponent))]
public sealed class ShadekinNightVisionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WgShadekinTestLight
  components:
  - type: PointLight
    radius: 6
    energy: 20
";

    /// <summary>Ticks for ShadekinSystem's once-a-second light check to run.</summary>
    private const int UpdateTicks = 90;

    [Test]
    public async Task NightVisionFollowsLightLevelTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var shadekin = EntityUid.Invalid;
        await server.WaitPost(() => shadekin = entMan.SpawnEntity("MobShadekin", map.GridCoords));

        // The test map has no lights.
        await server.WaitRunTicks(UpdateTicks);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ShadekinComponent>(shadekin).CurrentState, Is.EqualTo(ShadekinState.Dark));
            Assert.That(entMan.TryGetComponent(shadekin, out NightVisionComponent? nightVision) && nightVision.Enabled,
                Is.True, "A Shadekin in the dark has no active night vision.");
        });

        await server.WaitPost(() => entMan.SpawnEntity("WgShadekinTestLight", map.GridCoords));
        await server.WaitRunTicks(UpdateTicks);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<ShadekinComponent>(shadekin).CurrentState, Is.Not.EqualTo(ShadekinState.Dark),
                "Precondition: the test light should lift the Shadekin out of the Dark state.");
            Assert.That(entMan.GetComponent<NightVisionComponent>(shadekin).Enabled,
                Is.False, "A lit Shadekin still has night vision.");
        });

        await pair.CleanReturnAsync();
    }
}
