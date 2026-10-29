using Content.Server._DV.Feroxi;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>Feroxi never get thirsty, so they never dry out into their weaker dehydrated metabolism.</summary>
[TestFixture]
public sealed class FeroxiThirstTest
{
    [Test]
    public async Task FeroxiNeverGetThirsty()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid feroxi = default;
        var okay = 0f;
        var afterMinute = 0f;

        await server.WaitAssertion(() =>
        {
            feroxi = em.SpawnEntity("MobFeroxi", map.GridCoords);
            okay = em.GetComponent<ThirstComponent>(feroxi).ThirstThresholds[ThirstThreshold.Okay];
        });

        await server.WaitRunTicks(server.Timing.TickRate * 60);

        // Something that drains thirst, such as a Kelotane overdose, is undone before it can dry them out.
        await server.WaitAssertion(() =>
        {
            var thirst = em.GetComponent<ThirstComponent>(feroxi);
            afterMinute = thirst.CurrentThirst;
            em.System<ThirstSystem>().SetThirst(feroxi, thirst, 0f);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(afterMinute, Is.GreaterThanOrEqualTo(okay), "a Feroxi got thirsty over time.");
            Assert.That(em.GetComponent<ThirstComponent>(feroxi).CurrentThirst, Is.GreaterThanOrEqualTo(okay), "a drained Feroxi stayed thirsty.");
            Assert.That(em.GetComponent<FeroxiDehydrateComponent>(feroxi).Dehydrated, Is.False, "a Feroxi dried out.");
        });

        await pair.CleanReturnAsync();
    }
}
