#nullable enable
using System.Numerics;
using Content.Shared.Decals;
using Content.Shared._WF.RMCCore.Gibbing;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>A hurt xeno splatters blood, a dead one splatters more, and a battered corpse gibs.</summary>
[TestFixture]
public sealed class XenoBloodTest
{
    [Test]
    public async Task HitsDeathAndGibsLeaveBlood()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var damageable = entities.System<DamageableSystem>();
        var mobState = entities.System<MobStateSystem>();
        var map = await pair.CreateTestMap();

        EntityUid drone = default;
        var pos = new Vector2(0.5f, 0.5f);
        // the range query only looks in one chunk; count every decal on the small test grid instead
        int Count()
        {
            var total = 0;
            foreach (var chunk in entities.GetComponent<DecalGridComponent>(map.Grid).ChunkCollection.ChunkCollection.Values)
                total += chunk.Decals.Count;
            return total;
        }

        await server.WaitPost(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            for (var x = -2; x <= 2; x++)
            for (var y = -2; y <= 2; y++)
                maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
            drone = entities.SpawnEntity("CMXenoDrone", new EntityCoordinates(map.Grid, pos));
            // RMC's random gib-on-death would make the corpse step a coin flip
            var gibOnDeath = entities.GetComponent<RMCGibOnDeathComponent>(drone);
            gibOnDeath.GibChance = 0;
            gibOnDeath.DamageGibMultiplier = 0;
        });
        await pair.RunTicksSync(5);

        var brute = new DamageSpecifier(protoMan.Index<DamageGroupPrototype>("Brute"), 30);
        var start = 0;
        await server.WaitAssertion(() =>
        {
            start = Count();
            damageable.TryChangeDamage(drone, brute, true);
        });
        await pair.RunTicksSync(3);
        var afterHit = 0;
        await server.WaitAssertion(() =>
        {
            afterHit = Count();
            Assert.That(afterHit, Is.GreaterThan(start), "A hard hit leaves a blood splatter.");
            damageable.TryChangeDamage(drone, new DamageSpecifier(protoMan.Index<DamageGroupPrototype>("Brute"), 700), true);
            Assert.That(mobState.IsDead(drone), "The drone dies.");
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(Count(), Is.GreaterThan(afterHit + 1), "Death leaves several splatters.");
            Assert.That(entities.EntityExists(drone), "A dead drone is a corpse, not gibs.");
            damageable.TryChangeDamage(drone, new DamageSpecifier(protoMan.Index<DamageGroupPrototype>("Brute"), 600), true);
        });
        await pair.RunTicksSync(3);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(drone), "A corpse at twice its death damage gibs.");
        });

        await pair.CleanReturnAsync();
    }
}
