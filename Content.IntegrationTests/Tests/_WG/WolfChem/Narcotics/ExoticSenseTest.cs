using System.Numerics;
using Content.Server._WG.AsteroidMining;
using Content.Server._WG.Lattice;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Shared._WG.AsteroidMining;
using Content.Shared._WG.WolfChem.Narcotics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// The Chorus sense: a rock in the Coda that would drop exotic matter is pointed out once the high reaches it, a
/// rock in anchored space never is, the carrier gains Drift faster, and coming down ends it.
/// </summary>
[TestFixture]
[TestOf(typeof(WGExoticSenseSystem))]
public sealed class ExoticSenseTest
{
    [Test]
    public async Task ChorusPointsAtCodaRockTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid mob = default;

        await server.WaitAssertion(() =>
        {
            var sense = server.System<WGExoticSenseSystem>();
            var transform = server.System<SharedTransformSystem>();
            server.System<WGLatticeSystem>().Setup(map.MapUid);

            var coda = new Vector2(90000f, 0f);
            Assert.That(server.System<WGLatticeSystem>().ExoticAt(new MapCoordinates(coda, map.MapId)), Is.GreaterThan(0f));

            // A rich rock 700 m out in the Coda, and one just as rich in anchored space.
            var rock = SpawnRock(entMan, map.MapUid, coda + new Vector2(700f, 0f));
            SpawnRock(entMan, map.MapUid, new Vector2(60f, 0f));
            var rockAt = transform.GetMapCoordinates(rock).Position;

            Assert.That(sense.TryFindRock(new MapCoordinates(Vector2.Zero, map.MapId), 1500f, out _, out _), Is.False,
                "Anchored space holds no exotic matter.");

            mob = entMan.SpawnEntity("MobHuman", new MapCoordinates(coda, map.MapId));
            var comp = entMan.EnsureComponent<WGIntoxicationComponent>(mob);
            comp.Levels["WGChorus"] = 2f;
            sense.Tick();
            Assert.That(entMan.GetComponent<WGExoticSenseComponent>(mob).HasTarget, Is.False,
                "A light high doesn't reach 700 m.");

            var gain = new WGDriftGainEvent();
            entMan.EventBus.RaiseLocalEvent(mob, ref gain);
            Assert.That(gain.Multiplier, Is.EqualTo(WGExoticSenseSystem.DriftGain), "Sensing draws Drift faster.");

            comp.Levels["WGChorus"] = 8f;
            sense.Tick();
            var found = entMan.GetComponent<WGExoticSenseComponent>(mob);
            Assert.That(found.HasTarget, Is.True, "A heavy high reaches it.");
            Assert.That((found.Target - rockAt).Length(), Is.LessThan(0.1f));
            Assert.That(found.Richness, Is.EqualTo(Math.Clamp(100f * 0.06f / WGExoticSenseSystem.RichShards, 0.25f, 1f)).Within(0.01f));

            comp.Levels["WGChorus"] = 0f;
            sense.Tick();
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WGExoticSenseComponent>(mob), Is.False, "Coming down ends the sense.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Spawns a large asteroid with 100 veins and returns its rock.</summary>
    private static EntityUid SpawnRock(IEntityManager entMan, EntityUid mapUid, Vector2 position)
    {
        var grid = entMan.SpawnEntity("WGAsteroidLarge", new EntityCoordinates(mapUid, position));
        var rock = entMan.GetComponent<WGAsteroidGridComponent>(grid).RockUid!.Value;
        entMan.GetComponent<WGAsteroidComponent>(rock).Mass = 100f;
        return rock;
    }
}
