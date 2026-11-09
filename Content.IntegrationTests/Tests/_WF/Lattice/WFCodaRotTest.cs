using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Shared._WF.Lattice;
using Content.Shared.Damage;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>Out in the Coda's open space a person's flesh comes apart and an arm twists; back aboard it stops.</summary>
public sealed class WFCodaRotTest
{
    [Test]
    public async Task OpenCodaRotsFlesh()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var config = server.ResolveDependency<IConfigurationManager>();
        EntityUid mob = default;

        await server.WaitPost(() =>
        {
            config.SetCVar(WFCodaRotCVars.Grace, 2f);
            config.SetCVar(WFCodaRotCVars.Mutate, 4f);
            entities.System<WFLatticeSystem>().Setup(map.MapUid);
            mob = entities.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(90000f, 0f), map.MapId));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunSeconds(7f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WFCodaRotComponent>(mob), Is.True, "nobody noticed someone out in the open Coda.");
            var damage = entities.GetComponent<DamageableComponent>(mob).Damage.DamageDict;
            Assert.That(damage.TryGetValue("Cellular", out var cellular) && cellular > 0, Is.True, "the open Coda left their flesh alone.");
            Assert.That(entities.HasComponent<WFCodaMutatedComponent>(mob), Is.True, "an arm didn't twist out in the open Coda.");

            // Back aboard a hull, it stops and fades.
            entities.System<SharedTransformSystem>().SetCoordinates(mob, map.GridCoords);
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WFCodaRotComponent>(mob), Is.False, "the rot carried on aboard.");
            config.SetCVar(WFCodaRotCVars.Grace, WFCodaRotCVars.Grace.DefaultValue);
            config.SetCVar(WFCodaRotCVars.Mutate, WFCodaRotCVars.Mutate.DefaultValue);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });

        await pair.CleanReturnAsync();
    }
}
