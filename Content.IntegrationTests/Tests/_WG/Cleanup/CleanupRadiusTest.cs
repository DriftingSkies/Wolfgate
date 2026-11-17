using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Mono.Cleanup;

namespace Content.IntegrationTests.Tests._WG.Cleanup;

/// <summary>Invalid cleanup protection radii keep the entity instead of allowing cleanup.</summary>
public sealed class CleanupRadiusTest : InteractionTest
{
    [Test]
    public async Task InvalidRadiiPreserveEntitiesWithoutQuerying()
    {
        await Server.WaitAssertion(() =>
        {
            var cleanup = SEntMan.System<CleanupHelperSystem>();
            foreach (var radius in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                Assert.That(cleanup.HasNearbyPlayers(MapData.GridCoords, radius), Is.True,
                    $"Invalid radius {radius} must preserve player protection.");
                Assert.That(cleanup.HasNearbyGrids(MapData.GridCoords, radius), Is.True,
                    $"Invalid radius {radius} must preserve grid protection.");
            }

            Assert.That(cleanup.HasNearbyGrids(MapData.GridCoords, 1f), Is.True,
                "Positive radii must still detect the existing grid.");
        });
    }
}
