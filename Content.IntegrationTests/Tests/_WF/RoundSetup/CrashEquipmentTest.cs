using System.Linq;
using Content.Server._WF.RoundSetup;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Checks issued EVA protection, preserved clothing and species-specific emergency supplies.</summary>
[TestFixture]
public sealed class CrashEquipmentTest
{
    [TestCase("MobHuman", "BoxSurvivalMonoOxygenExtended", "DoubleEmergencyOxygenTankFilled")]
    [TestCase("MobVox", "BoxSurvivalMonoNitrogenExtended", "DoubleEmergencyNitrogenTankFilled")]
    [TestCase("MobFeroxi", "BoxSurvivalMonoVaporExtended", "DoubleEmergencyWaterVaporTankFilled")]
    public async Task SurvivorsReceiveProtectionAndCompatibleGas(string species, string boxId, string tankId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var inventory = pair.Server.System<InventorySystem>();
            var body = em.SpawnEntity(species, map.GridCoords);
            var coat = em.SpawnEntity("ClothingOuterCoatLab", map.GridCoords);
            Assert.That(inventory.TryEquip(body, coat, "outerClothing", silent: true, force: true), Is.True);
            pair.Server.System<WFCrashScenarioSystem>().GiveEmergencyEquipment(body);
            Assert.That(inventory.TryGetSlotEntity(body, "outerClothing", out var suit), Is.True);
            Assert.That(em.GetComponent<MetaDataComponent>(suit!.Value).EntityPrototype!.ID, Is.EqualTo("ClothingOuterSuitEmergency"));
            Assert.That(inventory.TryGetSlotEntity(body, "mask", out var mask), Is.True);
            Assert.That(em.GetComponent<MetaDataComponent>(mask!.Value).EntityPrototype!.ID, Is.EqualTo("ClothingMaskBreath"));
            Assert.That(inventory.TryGetSlotEntity(body, "back", out var bag), Is.True);
            var contents = em.GetComponent<StorageComponent>(bag!.Value).Container.ContainedEntities;
            Assert.That(contents, Does.Contain(coat), "Starting clothing must be preserved in the survival kit.");
            var box = contents.Single(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == boxId);
            var supplies = em.GetComponent<StorageComponent>(box).Container.ContainedEntities
                .Select(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID).ToArray();
            Assert.That(supplies, Does.Contain(tankId));
            Assert.That(supplies, Does.Contain("SpaceMedipen"));
            Assert.That(supplies, Does.Contain("MonoMRE"));
            em.DeleteEntity(body);
            em.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }
}
