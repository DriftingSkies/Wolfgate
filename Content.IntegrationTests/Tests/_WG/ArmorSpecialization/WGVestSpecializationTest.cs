using Content.Shared._Mono.ArmorPlate;
using Content.Shared._WG.ArmorSpecialization;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ArmorSpecialization;

/// <summary>The ported RMC armors carry their vest specializations, and those act on whoever wears them.</summary>
[TestFixture]
public sealed class WGVestSpecializationTest
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

            Assert.That(entities.HasComponent<ArmorPlateHolderComponent>(entities.SpawnEntity("WGRMCArmorM3", at)));
            var scout = entities.SpawnEntity("WGRMCArmorM3Scout", at);
            Assert.That(entities.HasComponent<WGLowSignatureComponent>(scout));
            Assert.That(entities.HasComponent<SpeedModifierContactCapClothingComponent>(scout));
            Assert.That(entities.HasComponent<WGUnrestrictedComponent>(entities.SpawnEntity("WGRMCArmorM3L", at)));
            Assert.That(entities.HasComponent<WGFastHandsComponent>(entities.SpawnEntity("WGRMCArmorUL6", at)));
            Assert.That(entities.HasComponent<WGSecondWindComponent>(entities.SpawnEntity("WGRMCArmorMilitiaVest", at)));

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
            var hands = entities.System<SharedHandsSystem>();
            var inventory = entities.System<InventorySystem>();

            // Gun first, then the vest goes on and comes off.
            var wearer = entities.SpawnEntity("MobHuman", at);
            var gun = entities.SpawnEntity("WeaponPistolMk58", at);
            var comp = entities.GetComponent<GunComponent>(gun);
            Assert.That(hands.TryPickupAnyHand(wearer, gun));
            var baseMax = comp.MaxAngleModified.Theta;
            var baseMin = comp.MinAngleModified.Theta;

            var vest = entities.SpawnEntity("WGRMCArmorM3L", at);
            Assert.That(inventory.TryEquip(wearer, vest, "outerClothing", force: true));
            Assert.That(comp.MaxAngleModified.Theta, Is.EqualTo(baseMax * 0.8).Within(1e-4), "equipping the vest left the held gun's spread alone.");
            Assert.That(comp.MinAngleModified.Theta, Is.EqualTo(baseMin * 0.8).Within(1e-4));

            Assert.That(inventory.TryUnequip(wearer, "outerClothing", force: true));
            Assert.That(comp.MaxAngleModified.Theta, Is.EqualTo(baseMax).Within(1e-4), "taking the vest off kept the tighter spread.");
            Assert.That(comp.MinAngleModified.Theta, Is.EqualTo(baseMin).Within(1e-4));

            // Vest first, then the gun is picked up.
            var other = entities.SpawnEntity("MobHuman", at);
            var otherVest = entities.SpawnEntity("WGRMCArmorM3L", at);
            Assert.That(inventory.TryEquip(other, otherVest, "outerClothing", force: true));
            var otherGun = entities.SpawnEntity("WeaponPistolMk58", at);
            Assert.That(hands.TryPickupAnyHand(other, otherGun));
            var otherComp = entities.GetComponent<GunComponent>(otherGun);
            Assert.That(otherComp.MaxAngleModified.Theta, Is.EqualTo(baseMax * 0.8).Within(1e-4), "a gun picked up in the vest missed its modifier.");
            Assert.That(otherComp.MinAngleModified.Theta, Is.EqualTo(baseMin * 0.8).Within(1e-4));

            entities.DeleteEntity(map);
        });

        await pair.CleanReturnAsync();
    }
}
