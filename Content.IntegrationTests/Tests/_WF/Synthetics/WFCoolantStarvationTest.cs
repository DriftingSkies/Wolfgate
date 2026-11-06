#nullable enable
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WF.Biotype;
using Content.Shared.Atmos;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>A chassis drained of fluid starves its core until it is refilled, and a starved core kills it.</summary>
[TestFixture]
public sealed class WFCoolantStarvationTest
{
    [Test]
    public async Task DrainedCoreStarvesUntilRefilled()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var life = entMan.System<WolfmedLifeSystem>();
        EntityUid body = default;
        float full = 0f, drained = 0f, settled = 0f, refilled = 0f;

        await server.WaitPost(() =>
        {
            Air(server, map.MapUid);
            body = SpawnSynthetic(entMan, map.GridCoords);
        });
        await pair.RunSeconds(1f);
        await server.WaitPost(() => full = life.GetBrainOrgan(body)!.Value.Comp.Health.Float());

        for (var i = 0; i < 10; i++)
        {
            await server.WaitPost(() => SetFluid(entMan, body, 0.2f));
            await pair.RunSeconds(1f);
        }

        await server.WaitPost(() =>
        {
            drained = life.GetBrainOrgan(body)!.Value.Comp.Health.Float();
            SetFluid(entMan, body, 1f);
        });
        // The fluid fraction Wolfmed reads lags a poll behind; let it settle before measuring.
        await pair.RunSeconds(2f);
        await server.WaitPost(() => settled = life.GetBrainOrgan(body)!.Value.Comp.Health.Float());
        await pair.RunSeconds(5f);
        await server.WaitPost(() => refilled = life.GetBrainOrgan(body)!.Value.Comp.Health.Float());
        TestContext.Out.WriteLine($"core {full} -> {drained} -> {settled} -> {refilled}");

        Assert.Multiple(() =>
        {
            Assert.That(drained, Is.LessThan(full), "a drained chassis's core didn't starve.");
            Assert.That(refilled, Is.EqualTo(settled).Within(0.01f), "the core kept starving after a refill.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StarvedCoreDies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid body = default;

        await server.WaitPost(() =>
        {
            Air(server, map.MapUid);
            body = SpawnSynthetic(entMan, map.GridCoords);
        });
        await pair.RunSeconds(1f);
        await server.WaitPost(() =>
        {
            var core = entMan.System<WolfmedLifeSystem>().GetBrainOrgan(body)!.Value;
            entMan.System<OrganHealthSystem>().SetHealth(core, FixedPoint2.New(0.2f));
        });

        var dead = false;
        for (var i = 0; i < 20 && !dead; i++)
        {
            await server.WaitPost(() =>
            {
                SetFluid(entMan, body, 0.2f);
                dead = entMan.System<MobStateSystem>().IsDead(body);
            });
            await pair.RunSeconds(1f);
        }

        Assert.That(dead, Is.True, "a chassis whose core starved to nothing is still alive.");
        await pair.CleanReturnAsync();
    }

    private static void SetFluid(IEntityManager entMan, EntityUid body, float fraction)
    {
        var blood = entMan.System<BloodstreamSystem>();
        FixedPoint2 volume = entMan.GetComponent<BloodstreamComponent>(body).BloodMaxVolume;
        var max = volume.Float();
        var current = blood.GetBloodLevelPercentage(body) * max;
        blood.TryModifyBloodLevel(body, FixedPoint2.New(max * fraction - current));
    }

    private static void Air(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid mapUid)
    {
        var moles = new float[Atmospherics.AdjustedNumberOfGases];
        moles[(int) Gas.Oxygen] = 21.824779f;
        moles[(int) Gas.Nitrogen] = 82.10312f;
        server.System<AtmosphereSystem>().SetMapAtmosphere(mapUid, false, new GasMixture(moles, Atmospherics.T20C));
    }

    private static EntityUid SpawnSynthetic(IEntityManager entMan, Robust.Shared.Map.EntityCoordinates coords)
    {
        var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
        return entMan.System<StationSpawningSystem>().SpawnPlayerMob(coords, job: null, profile, station: null);
    }
}
