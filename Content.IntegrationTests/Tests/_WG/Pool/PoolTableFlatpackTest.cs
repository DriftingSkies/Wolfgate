using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Pool;

/// <summary>The pool table flatpack unpacks into a table and two cues.</summary>
[TestFixture]
public sealed class PoolTableFlatpackTest
{
    [Test]
    public async Task FlatpackUnpacksIntoTableAndCues()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        EntityUid flatpack = default;

        await server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity(null, map.GridCoords);
            var tool = em.SpawnEntity("Multitool", map.GridCoords);
            flatpack = em.SpawnEntity("WGPoolTableFlatpack", map.GridCoords);
            var ev = new InteractUsingEvent(user, tool, flatpack, map.GridCoords);
            em.EventBus.RaiseLocalEvent(flatpack, ev);
            Assert.That(ev.Handled, Is.True);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(flatpack), Is.True, "The flatpack is used up.");
            Assert.That(Count(em, "DarkPackTableWoodBilliard"), Is.EqualTo(1));
            Assert.That(Count(em, "WGPoolCue"), Is.EqualTo(2));
        });

        await pair.CleanReturnAsync();
    }

    private static int Count(IEntityManager em, string id)
    {
        var count = 0;
        var query = em.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var meta))
        {
            if (meta.EntityPrototype?.ID == id)
                count++;
        }

        return count;
    }
}
