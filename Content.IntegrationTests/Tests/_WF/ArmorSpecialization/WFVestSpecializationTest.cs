using Content.Shared._Mono.ArmorPlate;
using Content.Shared._WF.ArmorSpecialization;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.ArmorSpecialization;

/// <summary>The ported RMC armors carry their vest specializations, and those act on whoever wears them.</summary>
[TestFixture]
public sealed class WFVestSpecializationTest
{
    [Test]
    public async Task ArmorsCarryTheirSpecializations()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var at = new MapCoordinates(0, 0, mapId);

            Assert.That(entities.HasComponent<ArmorPlateHolderComponent>(entities.SpawnEntity("WFRMCArmorM3", at)));
            var scout = entities.SpawnEntity("WFRMCArmorM3Scout", at);
            Assert.That(entities.HasComponent<WFLowSignatureComponent>(scout));
            Assert.That(entities.HasComponent<SpeedModifierContactCapClothingComponent>(scout));
            Assert.That(entities.HasComponent<WFUnrestrictedComponent>(entities.SpawnEntity("WFRMCArmorM3L", at)));
            Assert.That(entities.HasComponent<WFFastHandsComponent>(entities.SpawnEntity("WFRMCArmorUL6", at)));
            Assert.That(entities.HasComponent<WFSecondWindComponent>(entities.SpawnEntity("WFRMCArmorMilitiaVest", at)));

            entities.DeleteEntity(map);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnrestrictedTightensSpread()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap(out var mapId);
            var at = new MapCoordinates(0, 0, mapId);
            var wearer = entities.SpawnEntity("MobHuman", at);
            var vest = entities.SpawnEntity("WFRMCArmorM3L", at);
            Assert.That(entities.System<InventorySystem>().TryEquip(wearer, vest, "outerClothing", force: true));

            var ev = new GunRefreshModifiersEvent(default, null, 1f, new Angle(1), new Angle(1), new Angle(10), new Angle(2), 1, 1f, 1f, null);
            entities.EventBus.RaiseLocalEvent(wearer, ref ev);
            Assert.That(ev.MaxAngle.Theta, Is.EqualTo(8).Within(0.01));
            Assert.That(ev.MinAngle.Theta, Is.EqualTo(1.6).Within(0.01));

            entities.DeleteEntity(map);
        });

        await pair.CleanReturnAsync();
    }
}
