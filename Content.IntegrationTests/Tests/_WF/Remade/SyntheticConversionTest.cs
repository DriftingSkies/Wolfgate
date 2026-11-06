#nullable enable
using System.Linq;
using Content.Server._WF.Remade.Conversion;
using Content.Server.Station.Systems;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Remade;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>A dead synthetic rises as Remade the same as flesh does.</summary>
[TestFixture]
public sealed class SyntheticConversionTest
{
    private const string SyntheticHuman = "SyntheticHuman";

    [TestCase("MobHuman")]
    [TestCase("MobIPC")]
    [TestCase("MobSynth")]
    [TestCase(SyntheticHuman)]
    public async Task DeadBodyRises(string kind)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();
        EntityUid body = default;
        var started = false;

        await server.WaitPost(() => body = Spawn(entMan, kind, map.GridCoords));
        await pair.RunSeconds(1f);
        await server.WaitPost(() =>
        {
            entMan.System<MobStateSystem>().ChangeMobState(body, MobState.Dead);
            started = conversion.TryStartConversion(body);
        });
        Assert.That(started, Is.True, $"a dead {kind} can't start turning.");

        await pair.RunSeconds(14f);
        var risen = false;
        await server.WaitPost(() => risen = entMan.EntityQuery<WFRemadeComponent>().Any());
        Assert.That(risen, Is.True, $"a dead {kind} didn't rise.");
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
