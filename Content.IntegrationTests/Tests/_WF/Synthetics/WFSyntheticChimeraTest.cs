#nullable enable
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Polymorph.Components;
using Content.Server.Station.Systems;
using Content.Shared._WF.Biotype;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Synthetics;

/// <summary>Letoferol turns a dead synthetic, and the chassis filter flushes every other reagent.</summary>
[TestFixture]
public sealed class WFSyntheticChimeraTest
{
    private const string SyntheticHuman = "SyntheticHuman";

    [TestCase("MobIPC")]
    [TestCase("MobSynth")]
    [TestCase(SyntheticHuman)]
    public async Task DeadSyntheticTurns(string kind)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid victim = default;
        var added = false;

        await server.WaitPost(() => victim = Spawn(entMan, kind, map.GridCoords));
        await pair.RunSeconds(1f);
        await server.WaitPost(() =>
        {
            entMan.System<MobStateSystem>().ChangeMobState(victim, MobState.Dead);
            added = entMan.System<BloodstreamSystem>().TryAddToChemicals(victim, new Solution("NaturalLetoferol", FixedPoint2.New(25)));
        });
        Assert.That(added, Is.True, $"no Letoferol got into a {kind}.");

        await pair.RunTicksSync(150);

        var turned = false;
        await server.WaitPost(() =>
        {
            var query = entMan.EntityQueryEnumerator<PolymorphedEntityComponent>();
            while (query.MoveNext(out _, out var polymorph))
                turned |= polymorph.Parent == victim;
        });
        Assert.That(turned, Is.True, $"a dead {kind} full of Letoferol did not turn.");
        await pair.CleanReturnAsync();
    }

    [TestCase("MobIPC")]
    [TestCase(SyntheticHuman)]
    public async Task FilterFlushesOtherReagents(string kind)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid body = default;

        await server.WaitPost(() => body = Spawn(entMan, kind, map.GridCoords));
        await pair.RunSeconds(1f);
        await server.WaitPost(() =>
        {
            var bloodstream = entMan.System<BloodstreamSystem>();
            Assert.That(bloodstream.TryAddToChemicals(body, new Solution("ChloralHydrate", FixedPoint2.New(10))), Is.True);
            Assert.That(bloodstream.TryAddToChemicals(body, new Solution("Letoferol", FixedPoint2.New(5))), Is.True);
        });
        await pair.RunSeconds(3f);

        FixedPoint2 chloral = default, letoferol = default;
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<BloodstreamComponent>(body);
            Assert.That(entMan.System<SharedSolutionContainerSystem>().ResolveSolution(body, comp.ChemicalSolutionName,
                ref comp.ChemicalSolution, out var chemicals), Is.True);
            chloral = chemicals!.GetTotalPrototypeQuantity("ChloralHydrate");
            letoferol = chemicals.GetTotalPrototypeQuantity("Letoferol");
            entMan.DeleteEntity(body);
        });

        Assert.Multiple(() =>
        {
            Assert.That(chloral, Is.EqualTo(FixedPoint2.Zero), "the filter left a sedative in the chassis.");
            Assert.That(letoferol, Is.GreaterThan(FixedPoint2.Zero), "the filter flushed Letoferol.");
        });
        await pair.CleanReturnAsync();
    }

    private static EntityUid Spawn(IEntityManager entMan, string kind, EntityCoordinates coords)
    {
        if (kind != SyntheticHuman)
            return entMan.SpawnEntity(kind, coords);

        var profile = HumanoidCharacterProfile.RandomWithSpecies("Human").WithBiotype(WFBiotype.Synthetic);
        return entMan.System<StationSpawningSystem>().SpawnPlayerMob(coords, job: null, profile, station: null);
    }
}
