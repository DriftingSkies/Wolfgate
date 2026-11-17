using System.Collections.Generic;
using System.Numerics;
using Content.Server._WG.Lattice;
using Content.Server._WG.Remade.Conversion;
using Content.Shared._WG.DroneRings;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Lattice;

/// <summary>A body left dead in the Deep starts to turn Remade; one in anchored space, or on a sheltered ship, doesn't.</summary>
public sealed class WGDeepRisingTest
{
    [Test]
    public async Task DeadInTheDeepRise()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var lattice = entities.System<WGLatticeSystem>();
        var mobs = entities.System<MobStateSystem>();

        EntityUid deep = default, safe = default, sheltered = default;
        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);

            entities.System<SharedTransformSystem>().SetWorldPosition(map.Grid, new Vector2(72000f, 0f));
            sheltered = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EnsureComponent<Content.Shared._WG.Lattice.WGLatticeShelterComponent>(sheltered);
            deep = entities.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(72100f, 0f), map.MapId));
            safe = entities.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(10000f, 0f), map.MapId));
            foreach (var body in new[] { deep, safe, sheltered })
            {
                mobs.ChangeMobState(body, MobState.Dead);
            }
        });

        await pair.RunSeconds(75f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(deep) || entities.HasComponent<WGRemadeConvertingComponent>(deep), Is.True, "a body in the Deep didn't start to turn.");
            Assert.That(entities.HasComponent<WGRemadeConvertingComponent>(safe), Is.False, "a body in anchored space turned.");
            Assert.That(entities.HasComponent<WGRemadeConvertingComponent>(sheltered), Is.False, "a sheltered body turned.");
        });

        await pair.CleanReturnAsync();
    }
}
