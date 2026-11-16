using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SuitTank;

/// <summary>A hat covered by one suit's helmet survives another suit taking the head, and comes back afterwards.</summary>
[TestFixture]
public sealed class SuitPieceSwapTest
{
    [Test]
    public async Task ModsuitOverHardsuitKeepsHat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            var inventory = entMan.System<InventorySystem>();
            var modSuitSystem = entMan.System<ModSuitSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var mob = entMan.SpawnEntity("MobHuman", coords);
            var hat = entMan.SpawnEntity("ClothingHeadHatBeret", coords);
            var hardsuit = entMan.SpawnEntity("ClothingOuterHardsuitSpatio", coords);
            var modsuit = entMan.SpawnEntity("ClothingModsuitParamedic", coords);
            Assert.That(inventory.TryEquip(mob, hat, "head", silent: true, force: true));
            Assert.That(inventory.TryEquip(mob, hardsuit, "outerClothing", silent: true, force: true));
            Assert.That(inventory.TryEquip(mob, modsuit, "back", silent: true, force: true));

            entMan.EventBus.RaiseLocalEvent(hardsuit, new ToggleClothingEvent { Performer = mob });
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var hardsuitHelmet), Is.True);

            var modsuitComp = entMan.GetComponent<ModSuitComponent>(modsuit);
            Assert.That(modSuitSystem.TryDeployPart((modsuit, modsuitComp), "head", mob), Is.True,
                "the modsuit helmet should deploy over the hardsuit helmet");
            var modsuitHelmet = modSuitSystem.GetPart((modsuit, modsuitComp), "head");
            Assert.That(modsuitHelmet, Is.Not.Null);
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var head) && head == modsuitHelmet, Is.True,
                "the modsuit helmet should replace the hardsuit helmet");

            Assert.That(modSuitSystem.TryRetractPart((modsuit, modsuitComp), "head", mob), Is.True);
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var after) && after == hardsuitHelmet, Is.True,
                "the hardsuit helmet should come back");

            entMan.EventBus.RaiseLocalEvent(hardsuit, new ToggleClothingEvent { Performer = mob });
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var beret) && beret == hat, "the beret should be back on");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TakingOffHardsuitReturnsHat()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            var inventory = entMan.System<InventorySystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);
            var mob = entMan.SpawnEntity("MobHuman", coords);
            var hat = entMan.SpawnEntity("ClothingHeadHatBeret", coords);
            var hardsuit = entMan.SpawnEntity("ClothingOuterHardsuitSpatio", coords);
            Assert.That(inventory.TryEquip(mob, hat, "head", silent: true, force: true));
            Assert.That(inventory.TryEquip(mob, hardsuit, "outerClothing", silent: true, force: true));

            entMan.EventBus.RaiseLocalEvent(hardsuit, new ToggleClothingEvent { Performer = mob });
            Assert.That(inventory.TryUnequip(mob, "outerClothing", silent: true, force: true));
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var head) && head == hat, "the beret should be back on");
        });
        await pair.CleanReturnAsync();
    }
}
