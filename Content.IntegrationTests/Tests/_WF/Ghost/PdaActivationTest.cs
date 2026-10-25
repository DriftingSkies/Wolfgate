using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Ghost;

/// <summary>A PDA's wipe-only ghost role must not take E or Z away from opening the PDA.</summary>
[TestFixture]
public sealed class PdaActivationTest
{
    [Test]
    public async Task PdaOpensOnActivateAndUse()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var interaction = entMan.System<SharedInteractionSystem>();
            var ui = entMan.System<SharedUserInterfaceSystem>();
            var hands = entMan.System<SharedHandsSystem>();
            var inventory = entMan.System<InventorySystem>();
            var human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, human);

            var held = entMan.SpawnEntity("PassengerPDA", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(human, held));
            interaction.UseInHandInteraction(human, held);
            Assert.That(ui.IsUiOpen(held, PdaUiKey.Key, human), "Z on a held PDA should open it");
            ui.CloseUi(held, PdaUiKey.Key, human);

            interaction.InteractionActivate(human, held);
            Assert.That(ui.IsUiOpen(held, PdaUiKey.Key, human), "E on a held PDA should open it");

            var worn = entMan.SpawnEntity("PassengerPDA", map.GridCoords);
            Assert.That(inventory.TryEquip(human, worn, "id", silent: true, force: true));
            interaction.InteractionActivate(human, worn);
            Assert.That(ui.IsUiOpen(worn, PdaUiKey.Key, human), "E on a worn PDA should open it");
        });
        await pair.CleanReturnAsync();
    }
}
