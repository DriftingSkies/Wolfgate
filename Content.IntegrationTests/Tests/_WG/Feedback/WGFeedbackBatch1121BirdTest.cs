#nullable enable
using System.Linq;
using Content.Server.Storage.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Reports from the 2026-11-21 feedback export: hunting birds.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1121BirdTest
{
    /// <summary>#1249: a dead songbird goes in a crate; its flight hitbox used to make it too tall.</summary>
    [Test]
    public async Task SongbirdFitsInACrate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var storage = entMan.System<SharedEntityStorageSystem>();
            var crate = entMan.SpawnEntity("CrateGenericSteel", map.GridCoords);
            storage.CloseStorage(crate);
            Assert.That(entMan.GetComponent<EntityStorageComponent>(crate).Open, Is.False);

            var bird = entMan.SpawnEntity("WGMobBirdVerdaSongbird", map.GridCoords);
            entMan.System<MobStateSystem>().ChangeMobState(bird, MobState.Dead);
            Assert.That(storage.AddToContents(bird, crate), Is.True, "the dead songbird doesn't fit in a crate");
            Assert.That(entMan.GetComponent<EntityStorageComponent>(crate).Contents.Contains(bird), Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
