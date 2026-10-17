#nullable enable
using System.Collections.Generic;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Polymorph.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Chimera;

/// <summary>Fleshbeast claws get through armour, and every playable organic species can be turned.</summary>
[TestFixture]
public sealed class ChimeraConversionTest
{
    /// <summary>Armour that resists poison used to block every claw injection.</summary>
    [Test]
    public async Task ClawsInjectThroughArmour()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var victim = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var beast = entMan.SpawnEntity("MobLetoferolBeast", map.GridCoords);
            var suit = entMan.SpawnEntity("ClothingOuterBioGeneral", map.GridCoords);
            Assert.That(entMan.System<InventorySystem>().TryEquip(victim, suit, "outerClothing", force: true), Is.True);

            var hit = new MeleeHitEvent(new List<EntityUid> { victim }, beast, beast, new DamageSpecifier(), null);
            entMan.EventBus.RaiseLocalEvent(beast, hit);

            var bloodstream = entMan.GetComponent<BloodstreamComponent>(victim);
            Assert.That(entMan.System<SharedSolutionContainerSystem>().ResolveSolution(victim, bloodstream.ChemicalSolutionName,
                ref bloodstream.ChemicalSolution, out var chemicals), Is.True);
            Assert.That(chemicals!.GetTotalPrototypeQuantity("NaturalLetoferol"), Is.GreaterThan(FixedPoint2.Zero),
                "the bio suit's poison resistance blocked the fleshbeast's claws.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Avali and felionoids replaced the base species tags without Humanoid, which Letoferol requires.</summary>
    [TestCase("MobHuman")]
    [TestCase("MobAvali")]
    [TestCase("MobFelionoid")]
    public async Task DeadVictimTurns(string species)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var victim = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            victim = entMan.SpawnEntity(species, map.GridCoords);
            // Wolfmed bodies die of their wounds over time, not on a damage threshold.
            entMan.System<MobStateSystem>().ChangeMobState(victim, MobState.Dead);
            Assert.That(entMan.System<MobStateSystem>().IsDead(victim), Is.True);
            Assert.That(entMan.System<BloodstreamSystem>().TryAddToChemicals(victim,
                new Solution("NaturalLetoferol", FixedPoint2.New(30))), Is.True);
        });

        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            var turned = false;
            var query = entMan.EntityQueryEnumerator<PolymorphedEntityComponent>();
            while (query.MoveNext(out _, out var polymorph))
                turned |= polymorph.Parent == victim;
            Assert.That(turned, Is.True, $"a dead {species} full of Letoferol did not turn.");
        });
        await pair.CleanReturnAsync();
    }
}
