using System.Numerics;
using Content.Server._WG.Lattice;
using Content.Shared._WG.Lattice;
using Content.Shared.Damage;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Lattice;

/// <summary>Out in the Coda's open space a person's flesh comes apart and an arm twists; back aboard it stops.</summary>
public sealed class WGCodaRotTest
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
            config.SetCVar(WGCodaRotCVars.Grace, 2f);
            config.SetCVar(WGCodaRotCVars.Mutate, 4f);
            entities.System<WGLatticeSystem>().Setup(map.MapUid);
            mob = entities.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(90000f, 0f), map.MapId));
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunSeconds(7f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WGCodaRotComponent>(mob), Is.True, "nobody noticed someone out in the open Coda.");
            var damage = entities.GetComponent<DamageableComponent>(mob).Damage.DamageDict;
            Assert.That(damage.TryGetValue("Cellular", out var cellular) && cellular > 0, Is.True, "the open Coda left their flesh alone.");
            Assert.That(entities.HasComponent<WGCodaMutatedComponent>(mob), Is.True, "an arm didn't twist out in the open Coda.");

            // Back aboard a hull, it stops and fades.
            entities.System<SharedTransformSystem>().SetCoordinates(mob, map.GridCoords);
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WGCodaRotComponent>(mob), Is.False, "the rot carried on aboard.");
            config.SetCVar(WGCodaRotCVars.Grace, WGCodaRotCVars.Grace.DefaultValue);
            config.SetCVar(WGCodaRotCVars.Mutate, WGCodaRotCVars.Mutate.DefaultValue);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });

        await pair.CleanReturnAsync();
    }
}
