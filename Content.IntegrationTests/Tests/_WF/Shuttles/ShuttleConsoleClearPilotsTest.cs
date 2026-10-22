using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Shuttles;

/// <summary>Clearing a helm's pilots finishes even when an entry isn't a real pilot of that helm.</summary>
[TestFixture]
public sealed class ShuttleConsoleClearPilotsTest
{
    [Test]
    public async Task ClearPilotsSkipsEntriesItCannotRemove()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var console = em.SpawnEntity("ComputerShuttle", map.GridCoords);
            var helm = em.GetComponent<ShuttleConsoleComponent>(console);
            var stranger = em.SpawnEntity(null, map.GridCoords);
            helm.SubscribedPilots.Add(stranger);
            helm.SubscribedPilots.Add(console);

            // Before the fix this looped forever on the first entry and hung the server.
            em.System<ShuttleConsoleSystem>().ClearPilots(helm);
            Assert.That(helm.SubscribedPilots, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }
}
