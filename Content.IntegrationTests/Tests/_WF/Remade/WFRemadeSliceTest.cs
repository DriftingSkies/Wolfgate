#nullable enable
using System.Linq;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Power.Components;
using Content.Shared._WF.Remade;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The first slice: a Monolith gives power, climbs its stages, grows corruption, and corpses rise.</summary>
[TestFixture]
public sealed class WFRemadeSliceTest
{
    private const string Scion = "WFMonolithScion";

    [Test]
    public async Task DrawPaysPowerAndClimbsResonance()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var monoliths = entMan.System<WFMonolithSystem>();
        EntityUid uid = default;

        await server.WaitPost(() =>
        {
            uid = entMan.SpawnEntity(Scion, map.GridCoords);
            monoliths.SetDraw((uid, entMan.GetComponent<WFMonolithComponent>(uid)), 1f);
        });
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            var monolith = entMan.GetComponent<WFMonolithComponent>(uid);
            Assert.That(monolith.Resonance, Is.GreaterThan(0f));
            Assert.That(entMan.GetComponent<PowerSupplierComponent>(uid).MaxSupply, Is.GreaterThanOrEqualTo(2_000_000f));
            entMan.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyCentralBuysAScion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid uid = default;

        await server.WaitPost(() => uid = entMan.SpawnEntity(Scion, map.GridCoords));
        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<CargoSellBlacklistComponent>(uid), Is.True, "a Scion sells away from a main station.");
            Assert.That(entMan.System<PricingSystem>().GetPrice(uid), Is.GreaterThanOrEqualTo(5_000_000d));
            entMan.AddComponent<WFRemadeSanctuaryComponent>(map.Grid);
        });

        // On a main station it sells at any stage, for more the further it has climbed.
        await server.WaitPost(() => entMan.GetComponent<WFMonolithComponent>(uid).Resonance = 20f);
        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFMonolithComponent>(uid).Stage, Is.EqualTo(WFMonolithStage.Hymn));
            Assert.That(entMan.HasComponent<CargoSellBlacklistComponent>(uid), Is.False, "a Scion on a main station won't sell.");
            Assert.That(entMan.System<PricingSystem>().GetPrice(uid), Is.GreaterThanOrEqualTo(5_900_000d));
            entMan.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CorruptionSeedsAtGrowthAndDiesWithItsMonolith()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid uid = default;

        await server.WaitPost(() =>
        {
            uid = entMan.SpawnEntity(Scion, map.GridCoords);
            entMan.GetComponent<WFMonolithComponent>(uid).Resonance = 40f;
        });
        await pair.RunSeconds(6f);

        await server.WaitAssertion(() =>
        {
            var monolith = entMan.GetComponent<WFMonolithComponent>(uid);
            Assert.That(monolith.Corruption, Is.Not.Empty, "no corruption seeded at Growth.");
            Assert.That(monolith.Corruption.Count, Is.LessThanOrEqualTo(WFCorruptionSystem.Limit(WFMonolithStage.Growth, entMan.System<WFCorruptionSystem>().HullTiles(map.Grid.Owner))));
            var tile = monolith.Corruption.First();
            Assert.That(entMan.GetComponent<WFCorruptionComponent>(tile).Source, Is.EqualTo(uid));

            entMan.DeleteEntity(uid);
            Assert.That(entMan.GetComponent<WFCorruptionComponent>(tile).DecayAt, Is.Not.Null, "corruption didn't start dying with its Monolith.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACorpseRisesAndDropsItsGear()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();
        EntityUid corpse = default;
        EntityUid suit = default;

        await server.WaitPost(() =>
        {
            corpse = entMan.SpawnEntity("MobHuman", map.GridCoords);
            suit = entMan.SpawnEntity("ClothingOuterBioGeneral", map.GridCoords);
            Assert.That(entMan.System<InventorySystem>().TryEquip(corpse, suit, "outerClothing", force: true));
            Assert.That(conversion.CanConvert(corpse), Is.False, "a living body must not turn.");
            entMan.System<MobStateSystem>().ChangeMobState(corpse, Content.Shared.Mobs.MobState.Dead);
            Assert.That(conversion.TryStartConversion(corpse), Is.True);
        });

        await pair.RunSeconds(14f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(corpse) || entMan.IsQueuedForDeletion(corpse), Is.True, "the corpse wasn't used up.");
            Assert.That(entMan.Deleted(suit), Is.False, "gear must survive the conversion.");
            Assert.That(entMan.EntityQuery<WFRemadeComponent>().Any(), Is.True, "nothing rose.");
            var containers = entMan.System<Robust.Shared.Containers.SharedContainerSystem>();
            Assert.That(entMan.EntityQuery<Content.Shared.Body.Organ.OrganComponent>()
                    .Count(organ => !entMan.IsQueuedForDeletion(organ.Owner) && !containers.IsEntityInContainer(organ.Owner)),
                Is.Zero, "the burst corpse spilled its organs.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AGibbedRemadeLeavesNoOrgans()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid limb = default;

        await server.WaitPost(() =>
        {
            var remade = entMan.SpawnEntity("WFRemadeSlasher", map.GridCoords);
            limb = entMan.SpawnEntity("WFRemadeSeveredLimb", map.GridCoords);
            entMan.System<WFRemadeDirectorSystem>().Enlist(remade, null);
            entMan.System<Content.Server.Body.Systems.BodySystem>().GibBody(remade, gibOrgans: true);
        });

        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityQuery<Content.Shared.Body.Organ.OrganComponent>().Any(), Is.False, "a gibbed Remade spilled organs.");
            Assert.That(entMan.EntityQuery<Content.Shared.Body.Part.BodyPartComponent>().Any(), Is.False, "a gibbed Remade spilled body parts.");
            Assert.That(entMan.Deleted(limb) || entMan.IsQueuedForDeletion(limb), Is.False, "a severed limb on the floor went with the gib.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABodyWithoutAHeadWillNotTurn()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();

        await server.WaitAssertion(() =>
        {
            var corpse = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<MobStateSystem>().ChangeMobState(corpse, Content.Shared.Mobs.MobState.Dead);
            Assert.That(conversion.CanConvert(corpse), Is.True);

            var body = entMan.System<Content.Server.Body.Systems.BodySystem>();
            foreach (var (head, _) in body.GetBodyChildrenOfType(corpse, Content.Shared.Body.Part.BodyPartType.Head).ToList())
            {
                entMan.DeleteEntity(head);
            }

            Assert.That(conversion.CanConvert(corpse), Is.False);
            entMan.DeleteEntity(corpse);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheRemadeOfALostMonolithStarve()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid remade = default;

        await server.WaitPost(() =>
        {
            var scion = entMan.SpawnEntity(Scion, map.GridCoords);
            remade = entMan.SpawnEntity("WFRemadeSlasher", map.GridCoords);
            entMan.System<WFRemadeDirectorSystem>().Enlist(remade, scion);
            entMan.DeleteEntity(scion);
        });
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DamageableComponent>(remade).TotalDamage.Float(), Is.GreaterThan(0f));
            entMan.DeleteEntity(remade);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryMobEntryIsARemade()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            foreach (var mob in proto.EnumeratePrototypes<WFRemadeMobPrototype>())
            {
                Assert.That(proto.Index(mob.Entity).TryComp<WFRemadeVoiceComponent>(out _, server.EntMan.ComponentFactory), Is.True, mob.ID);
            }
        });
        await pair.CleanReturnAsync();
    }
}
