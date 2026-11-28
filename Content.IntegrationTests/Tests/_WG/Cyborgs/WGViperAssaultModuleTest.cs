using System;
using System.Linq;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Wieldable.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>Checks the NCI AK-220 module's role, weapons and automatic ammunition behavior.</summary>
[TestFixture]
public sealed class WGViperAssaultModuleTest
{
    private const string Module = "WGNCIAK220Module";
    private const string Weapon = "WGNCIAK220";
    private const string Cartridge = "WGNCIAK220Cartridge";
    private const string Machete = "Machete";
    private const string AssaultType = "WGViperAssault";
    private const string EngineeringType = "WGViperEngineering";
    private const string MedicalType = "WGViperMedical";

    [Test]
    public async Task AssaultGetsSelfRefillingCaselessAk220()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var assault = prototypes.Index<BorgTypePrototype>(AssaultType);
            var engineering = prototypes.Index<BorgTypePrototype>(EngineeringType);
            var medical = prototypes.Index<BorgTypePrototype>(MedicalType);

            Assert.That(assault.DefaultModules.Any(id => id.ToString() == Module), Is.True);
            Assert.That(engineering.DefaultModules.Any(id => id.ToString() == Module), Is.False);
            Assert.That(medical.DefaultModules.Any(id => id.ToString() == Module), Is.False);

            var module = entMan.SpawnEntity(Module, map.GridCoords);
            Assert.That(
                entMan.GetComponent<ItemBorgModuleComponent>(module).Items,
                Is.EquivalentTo(new[] { Weapon, Machete }));

            var weapon = entMan.SpawnEntity(Weapon, map.GridCoords);
            Assert.That(entMan.HasComponent<ItemSlotsComponent>(weapon), Is.False);
            Assert.That(entMan.HasComponent<ChamberMagazineAmmoProviderComponent>(weapon), Is.False);
            Assert.That(entMan.GetComponent<WieldableComponent>(weapon).FreeHandsRequired, Is.Zero);
            Assert.That(entMan.HasComponent<GunWieldBonusComponent>(weapon), Is.True);

            var ammo = entMan.GetComponent<BallisticAmmoProviderComponent>(weapon);
            Assert.That(ammo.Proto?.ToString(), Is.EqualTo(Cartridge));
            Assert.That(ammo.AutoCycle, Is.True);
            Assert.That(ammo.Cycleable, Is.False);
            var refiller = entMan.GetComponent<BallisticAmmoSelfRefillerComponent>(weapon);
            Assert.That(refiller.AutoRefill, Is.True);
            Assert.That(refiller.AutoRefillRate, Is.GreaterThan(TimeSpan.Zero));

            var cartridge = entMan.SpawnEntity(Cartridge, map.GridCoords);
            Assert.That(entMan.GetComponent<CartridgeAmmoComponent>(cartridge).DeleteOnSpawn, Is.True);

            entMan.DeleteEntity(module);
            entMan.DeleteEntity(weapon);
            entMan.DeleteEntity(cartridge);
        });

        await pair.CleanReturnAsync();
    }
}
