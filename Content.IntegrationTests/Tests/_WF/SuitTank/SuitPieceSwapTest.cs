using Content.Shared.Clothing.Components;
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
            entMan.EventBus.RaiseLocalEvent(modsuit, new ToggleClothingEvent { Performer = mob });
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var head)
                && entMan.TryGetComponent<AttachedClothingComponent>(head, out var piece) && piece.AttachedUid == modsuit,
                "the modsuit helmet should replace the hardsuit helmet");

            entMan.EventBus.RaiseLocalEvent(modsuit, new ToggleClothingEvent { Performer = mob });
            Assert.That(inventory.TryGetSlotEntity(mob, "head", out var after) && after == hat, "the beret should be back on");
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
