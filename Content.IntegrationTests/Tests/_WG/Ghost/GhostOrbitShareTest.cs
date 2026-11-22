using System.Linq;
using Content.Server._WG.Ghost;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Ghost;

/// <summary>Checks one orbit list serves every ghost for its share window, leaving out only whoever asks.</summary>
[TestFixture]
[TestOf(typeof(GhostOrbitSystem))]
public sealed class GhostOrbitShareTest
{
    [Test]
    public async Task ListIsSharedThenRebuilt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var orbit = server.System<GhostOrbitSystem>();
        var map = await pair.CreateTestMap();
        var before = server.CfgMan.GetCVar(WGGhostOrbitCVars.ShareSeconds);

        EntityUid first = default, second = default, late = default;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WGGhostOrbitCVars.ShareSeconds, 2f);
            // Mobs, since a ghost with no player is in no section; the viewer can be anything.
            first = entMan.SpawnEntity("MobHuman", map.GridCoords);
            second = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var forFirst = orbit.SharedTargets(first, false);
            late = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var forSecond = orbit.SharedTargets(second, false);

            var netFirst = entMan.GetNetEntity(first);
            var netSecond = entMan.GetNetEntity(second);
            Assert.That(forFirst.Any(t => t.Entity == netFirst), Is.False, "A ghost isn't offered itself.");
            Assert.That(forFirst.Any(t => t.Entity == netSecond), Is.True);
            Assert.That(forSecond.Any(t => t.Entity == netSecond), Is.False, "The shared list leaves out the second asker too.");
            Assert.That(forSecond.Any(t => t.Entity == netFirst), Is.True);
            Assert.That(forSecond.Any(t => t.Entity == entMan.GetNetEntity(late)), Is.False,
                "Within the window the second ghost gets the list built for the first.");
        });

        // Three seconds of game time is past the two-second window.
        await pair.RunTicksSync((int) (server.Timing.TickRate * 3));

        await server.WaitAssertion(() =>
        {
            var rebuilt = orbit.SharedTargets(second, false);
            Assert.That(rebuilt.Any(t => t.Entity == entMan.GetNetEntity(late)), Is.True, "After the window the list is built again.");

            server.CfgMan.SetCVar(WGGhostOrbitCVars.ShareSeconds, before);
            entMan.DeleteEntity(first);
            entMan.DeleteEntity(second);
            entMan.DeleteEntity(late);
        });

        await pair.CleanReturnAsync();
    }
}
