#nullable enable
using Content.Server.NPC.HTN;
using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared._WF.Xenonids.Weeds;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Xenonids;

/// <summary>The admin facehugger variants: no off-weeds decay, a bite, still mortal, and the AI ones hunt or stop as expected.</summary>
[TestFixture]
public sealed class XenoParasiteVariantTest
{
    [Test]
    public async Task HardyHuggerBitesAndStillDies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var damageable = entities.System<DamageableSystem>();
        var mobState = entities.System<MobStateSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "WFXenoParasiteHardy", "WFXenoParasiteHunterBiter", "WFXenoParasiteHunterInfector" })
            {
                var hugger = entities.SpawnEntity(id, map.GridCoords);
                var offWeeds = entities.GetComponent<DamageOffWeedsComponent>(hugger).Damage;
                Assert.That(offWeeds.GetTotal(), Is.EqualTo(FixedPoint2.Zero), $"{id} does not wither off weeds.");
                var bite = entities.GetComponent<MeleeWeaponComponent>(hugger).Damage;
                Assert.That(bite.GetTotal(), Is.GreaterThan(FixedPoint2.Zero), $"{id} bites.");

                var brute = new DamageSpecifier(protoMan.Index<DamageGroupPrototype>("Brute"), 100);
                damageable.TryChangeDamage(hugger, brute, true);
                Assert.That(mobState.IsDead(hugger), $"{id} still dies to damage.");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BiteOnlyHuggerNeverInfects()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var parasites = entities.System<SharedXenoParasiteSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hugger = entities.SpawnEntity("WFXenoParasiteHunterBiter", map.GridCoords);
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var comp = entities.GetComponent<XenoParasiteComponent>(hugger);

            Assert.That(parasites.Infect((hugger, comp), human, popup: false, force: true), Is.False, "A bite-only hugger refuses to infect.");
            Assert.That(entities.GetComponent<InfectableComponent>(human).BeingInfected, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HunterHuggerHuntsUntilItHugsOrDies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var parasites = entities.System<SharedXenoParasiteSystem>();
        var damageable = entities.System<DamageableSystem>();
        var map = await pair.CreateTestMap();

        EntityUid infector = default;
        EntityUid biter = default;
        await server.WaitAssertion(() =>
        {
            infector = entities.SpawnEntity("WFXenoParasiteHunterInfector", map.GridCoords);
            biter = entities.SpawnEntity("WFXenoParasiteHunterBiter", map.GridCoords);
            Assert.That(entities.HasComponent<HTNComponent>(infector), "A fresh hunter has its HTN.");
            Assert.That(entities.HasComponent<HTNComponent>(biter));

            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var comp = entities.GetComponent<XenoParasiteComponent>(infector);
            Assert.That(parasites.Infect((infector, comp), human, popup: false, force: true), "The infecting hunter hugs a host.");

            var brute = new DamageSpecifier(protoMan.Index<DamageGroupPrototype>("Brute"), 100);
            damageable.TryChangeDamage(biter, brute, true);
        });

        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<HTNComponent>(infector), Is.False, "On a face the hunter stops thinking.");
            Assert.That(entities.HasComponent<HTNComponent>(biter), Is.False, "A dead hunter stops thinking.");
        });

        await pair.CleanReturnAsync();
    }
}
