using System.Linq;
using System.Numerics;
using Content.Server._WG.Hope;
using Content.Shared._WG.Hope;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Hope;

/// <summary>The Sultan's altar sings to those near it and to nobody else, and every hymn it sings exists.</summary>
public sealed class WGHymnTest
{
    [Test]
    public async Task AltarSingsWithinItsReach()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var transform = entities.System<SharedTransformSystem>();
        var hymns = entities.System<WGHymnSourceSystem>();

        await server.WaitAssertion(() =>
        {
            var altar = entities.SpawnEntity("WGAltarSultan", map.GridCoords);
            var at = transform.GetWorldPosition(altar);
            var near = entities.SpawnEntity("MobHuman", new MapCoordinates(at + new Vector2(3f, 0f), map.MapId));
            var far = entities.SpawnEntity("MobHuman", new MapCoordinates(at + new Vector2(40f, 0f), map.MapId));

            hymns.Hear(near);
            hymns.Hear(far);
            Assert.That(entities.TryGetComponent<WGHymnListenerComponent>(near, out var listener), Is.True, "nobody heard the altar beside it.");
            Assert.That(listener!.Source, Is.EqualTo("Sultan"));
            Assert.That(listener.Near, Is.InRange(0.25f, 1f));
            Assert.That(entities.HasComponent<WGHymnListenerComponent>(far), Is.False, "the altar was heard 40 m away.");

            entities.GetComponent<WGHymnSourceComponent>(altar).Silent = true;
            hymns.Hear(near);
            Assert.That(listener.Source, Is.Null, "a silent altar still sang.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SultanHymnsExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var sultan = protos.EnumeratePrototypes<WGHymnPrototype>().Where(h => h.Source == "Sultan").ToList();
            Assert.That(sultan, Is.Not.Empty, "the Sultan has no hymns.");
            foreach (var hymn in sultan)
            {
                Assert.That(resources.ContentFileExists(hymn.Path), $"{hymn.ID} has no audio at {hymn.Path}.");
                Assert.That(hymn.Seconds, Is.GreaterThan(10f), $"{hymn.ID} is too short to be a hymn.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
