#nullable enable
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

/// <summary>
/// Crew upkeep: a revived deckhand gets his repair tool back and climbs out of the bed he was left in, a stripped
/// crewman puts his own clothes back on, and a janitor bins a spent medipen.
/// </summary>
public sealed partial class WGCrewLifeTest
{
    [Test]
    public async Task RevivedCrewGetBackToWork()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        EntityUid deckhand = default;
        await scene.Pair.Server.WaitAssertion(() =>
        {
            var mobs = entMan.System<MobStateSystem>();
            deckhand = Crewman(entMan, scene, "WGCrewDeckhand");
            Assert.That(entMan.HasComponent<ShipRepairToolComponent>(deckhand), Is.True, "Precondition: a deckhand has his repair tool.");

            mobs.ChangeMobState(deckhand, MobState.Dead);
            Assert.That(entMan.HasComponent<ShipRepairToolComponent>(deckhand), Is.False, "Precondition: his body is no endless tool.");
            mobs.ChangeMobState(deckhand, MobState.Alive);
            Assert.That(entMan.HasComponent<ShipRepairToolComponent>(deckhand), Is.True, "Revived, he has it back.");

            // Getting up is tried on a crewman who never died: a forced revival leaves Wolfmed's consciousness to
            // catch up, which is the medic's business, not this test's.
            deckhand = Crewman(entMan, scene, "WGCrewDeckhand", -1, -1);
            var bedAt = scene.Origin.Offset(new System.Numerics.Vector2(1, 1));
            var bed = entMan.SpawnEntity("MedicalBed", bedAt);
            entMan.System<SharedTransformSystem>().SetCoordinates(deckhand, bedAt);
            Assert.That(entMan.System<SharedBuckleSystem>().TryBuckle(deckhand, deckhand, bed, popup: false), Is.True,
                "Precondition: the medic left him in a bed.");
            Assert.That(entMan.System<WGCrewMedicSystem>().IsDown(deckhand), Is.False, "Precondition: he is up.");
        });

        // Past the buckle's unbuckle delay.
        await scene.Pair.RunTicksSync(60);
        await scene.Pair.Server.WaitAssertion(() =>
        {
            entMan.System<WGCrewRoutineSystem>().GetUp(deckhand);
            Assert.That(entMan.GetComponent<BuckleComponent>(deckhand).BuckledTo, Is.Null, "Well and nobody seeing to him, he gets up.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task StrippedCrewDressAgain()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        await scene.Pair.Server.WaitAssertion(() =>
        {
            var inventory = entMan.System<InventorySystem>();
            var outfits = entMan.System<WGCrewOutfitSystem>();
            var janitor = Crewman(entMan, scene, "WGCrewJanitor");
            outfits.Remember(janitor);
            Assert.That(inventory.TryGetSlotEntity(janitor, "shoes", out var shoes), Is.True, "Precondition: he wears shoes.");
            Assert.That(inventory.TryUnequip(janitor, "shoes", silent: true, force: true), Is.True, "Precondition: they come off.");
            Assert.That(inventory.TryGetSlotEntity(janitor, "shoes", out _), Is.False);

            Assert.That(outfits.Destination(janitor, out _), Is.True, "He goes for his own shoes.");
            outfits.Perform(janitor);
            Assert.That(inventory.TryGetSlotEntity(janitor, "shoes", out var worn) && worn == shoes, Is.True, "And puts them back on.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task JanitorsBinSpentPens()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        await scene.Pair.Server.WaitAssertion(() =>
        {
            var tidy = entMan.System<WGCrewTidySystem>();
            var janitor = Crewman(entMan, scene, "WGCrewJanitor");
            var bin = entMan.SpawnEntity("DisposalUnit", scene.Origin.Offset(new System.Numerics.Vector2(0, 1)));
            if (!entMan.GetComponent<TransformComponent>(bin).Anchored)
                entMan.System<SharedTransformSystem>().AnchorEntity(bin);
            var pen = entMan.SpawnEntity("BruteAutoInjector", scene.Origin.Offset(new System.Numerics.Vector2(1, 0)));
            entMan.System<TagSystem>().AddTag(pen, "Trash");

            Assert.That(tidy.Destination(janitor, out _), Is.True, "The janitor goes for the spent pen.");
            tidy.Perform(janitor);
            Assert.That(entMan.System<SharedHandsSystem>().IsHolding(janitor, pen, out _), Is.True, "He picks it up.");
            tidy.Perform(janitor);
            Assert.That(entMan.System<SharedHandsSystem>().IsHolding(janitor, pen, out _), Is.False, "He lets go of it at the bin.");
            Assert.That(entMan.System<SharedContainerSystem>().TryGetContainingContainer(pen, out var container) && container.Owner == bin,
                Is.True, "It is in the bin.");
        });

        await scene.Pair.CleanReturnAsync();
    }
}
