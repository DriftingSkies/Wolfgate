#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._RMC14.Weapons.Ranged.Prediction;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared._WF.Vehicles.Weapons;
using Content.Shared._WF.Vehicles.Weapons.BulletBox;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Explosion.Components;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Whitelist;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>The prototypes of the armed roster agree with each other: ammo boxes, guns, cartridges and rounds.</summary>
[TestFixture]
public sealed class VehicleArmedDataTest
{
    [Test]
    public async Task EveryAmmoBoxNamesAGunAndEveryGunHasABox()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var guns = new List<EntityPrototype>();
            var boxes = new List<EntityPrototype>();

            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || proto.ID.Contains("Test"))
                    continue;

                if (proto.TryGetComponent<HardpointItemComponent>(out _, factory) && proto.TryGetComponent<GunComponent>(out _, factory))
                    guns.Add(proto);

                if (proto.TryGetComponent<BulletBoxComponent>(out _, factory) && proto.ID.StartsWith("VehicleAmmoBox"))
                    boxes.Add(proto);
            }

            Assert.That(guns, Has.Count.GreaterThanOrEqualTo(25), "The armed hardpoints are missing.");
            Assert.That(boxes, Has.Count.GreaterThanOrEqualTo(10), "The vehicle ammo boxes are missing.");

            var named = new HashSet<string>();

            foreach (var gun in guns)
            {
                using (Assert.EnterMultipleScope())
                {
                    var ballistic = gun.TryGetComponent<BallisticAmmoProviderComponent>(out var ammo, factory);
                    Assert.That(ballistic, Is.True, $"{gun.ID}: a hardpoint gun feeds from a ballistic provider.");
                    Assert.That(gun.TryGetComponent<VehicleHardpointAmmoComponent>(out var hardpointAmmo, factory), Is.True, $"{gun.ID}: no reserve magazines are kept for it.");
                    Assert.That(gun.TryGetComponent<RefillableByBulletBoxComponent>(out var refill, factory), Is.True, $"{gun.ID}: no ammo box refills it.");
                    Assert.That(gun.TryGetComponent<GunComponent>(out var gunComp, factory), Is.True);

                    if (!ballistic || hardpointAmmo == null || refill == null || gunComp == null)
                        continue;

                    Assert.That(ammo!.Cycleable, Is.False, $"{gun.ID}: a hardpoint gun cannot be cycled.");
                    Assert.That(gunComp.Recoil, Is.EqualTo(0f), $"{gun.ID}: a mounted gun has no recoil.");
                    Assert.That(gunComp.ProjectileSpeed, Is.GreaterThan(0f), $"{gun.ID}: no round speed.");
                    Assert.That(hardpointAmmo.MagazineSize, Is.EqualTo(ammo.Capacity), $"{gun.ID}: the magazine size is not the capacity of the ready magazine.");
                    Assert.That(ammo.Proto, Is.Not.Null, $"{gun.ID}: no round to load.");

                    if (ammo.Proto is { } cartridgeId)
                        CheckCartridge(protos, factory, gun.ID, cartridgeId);

                    // The gun system raises its ammo event on the server only, so a round that is sent to a point is the server's
                    // alone: the shooter's client does not fire a copy of it.
                    if (gun.TryGetComponent<ShootAtFixedPointComponent>(out _, factory) && ammo.Proto is { } fixedCartridgeId)
                    {
                        var fixedCartridge = protos.Index<EntityPrototype>(fixedCartridgeId);
                        var roundId = fixedCartridge.TryGetComponent<CartridgeAmmoComponent>(out var fixedAmmo, factory) ? fixedAmmo.Prototype : default;
                        var round = protos.Index<EntityPrototype>(roundId);
                        Assert.That(round.TryGetComponent<IgnorePredictionHideComponent>(out _, factory), Is.True,
                            $"{gun.ID}: its rounds are sent to a point on the server, so the client must not predict them (IgnorePredictionHide).");
                    }

                    var boxId = refill.BulletType!.Value;
                    named.Add(boxId);
                    Assert.That(protos.TryIndex<EntityPrototype>(boxId, out var box), Is.True, $"{gun.ID}: the box {boxId} does not exist.");
                    if (box == null)
                        continue;

                    Assert.That(box.TryGetComponent<BulletBoxComponent>(out var boxComp, factory), Is.True, $"{gun.ID}: {boxId} is not a bullet box.");
                    if (boxComp == null)
                        continue;

                    var boxBullet = boxComp.BulletType;
                    Assert.That(boxBullet.ToString(), Is.EqualTo(boxId.ToString()), $"{boxId}: a box fills guns with its own prototype id.");
                    Assert.That(boxComp.Max, Is.EqualTo(hardpointAmmo.MagazineSize), $"{boxId}: a box holds one magazine of {gun.ID}.");
                }
            }

            // Every box is for a gun.
            foreach (var box in boxes)
            {
                Assert.That(named, Does.Contain(box.ID), $"{box.ID}: no hardpoint gun takes this box.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A gun shoots a cartridge that leaves no casing and a round that is a real projectile.</summary>
    private static void CheckCartridge(IPrototypeManager protos, IComponentFactory factory, string gunId, EntProtoId cartridgeId)
    {
        Assert.That(protos.TryIndex<EntityPrototype>(cartridgeId, out var cartridge), Is.True, $"{gunId}: the cartridge {cartridgeId} does not exist.");
        if (cartridge == null)
            return;

        Assert.That(cartridge.TryGetComponent<CartridgeAmmoComponent>(out var cartridgeAmmo, factory), Is.True, $"{gunId}: {cartridgeId} is not a cartridge.");
        if (cartridgeAmmo == null)
            return;

        Assert.That(cartridgeAmmo.DeleteOnSpawn, Is.True, $"{cartridgeId}: a casing arriving on a client trips a test-client assert.");
        Assert.That(protos.TryIndex<EntityPrototype>(cartridgeAmmo.Prototype, out var round), Is.True, $"{cartridgeId}: the round {cartridgeAmmo.Prototype} does not exist.");
        if (round != null)
            Assert.That(round.TryGetComponent<ProjectileComponent>(out _, factory), Is.True, $"{cartridgeId}: {cartridgeAmmo.Prototype} is not a projectile.");
    }

    /// <summary>
    /// A vehicle with no weapons seat has no use for a gun: none of its slots takes a part that fires or carries one (a turret body
    /// has gun slots of its own), so a gun can never sit inert in an unarmed vehicle.
    /// </summary>
    [Test]
    public async Task NoVehicleWithoutWeaponsTakesAGun()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var vehicles = new List<EntityPrototype>();
            var gunParts = new List<EntityPrototype>();

            foreach (var proto in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || proto.ID.Contains("Test"))
                    continue;

                if (proto.TryGetComponent<RMCVehicleComponent>(out _, factory) &&
                    proto.TryGetComponent<HardpointSlotsComponent>(out _, factory) &&
                    !proto.TryGetComponent<VehicleWeaponsComponent>(out _, factory))
                {
                    vehicles.Add(proto);
                }

                if (proto.TryGetComponent<HardpointItemComponent>(out _, factory) &&
                    (proto.TryGetComponent<GunComponent>(out _, factory) || proto.TryGetComponent<HardpointSlotsComponent>(out _, factory)))
                {
                    gunParts.Add(proto);
                }
            }

            Assert.That(vehicles, Is.Not.Empty, "No vehicle without the weapons component was found to check.");
            Assert.That(gunParts, Has.Count.GreaterThanOrEqualTo(25), "The armed hardpoints are missing.");

            var failures = new List<string>();
            foreach (var vehicle in vehicles)
            {
                var slots = vehicle.TryGetComponent<HardpointSlotsComponent>(out var vehicleSlots, factory) ? vehicleSlots : null;
                if (slots == null)
                    continue;

                foreach (var slot in slots.Slots)
                {
                    foreach (var part in gunParts)
                    {
                        if (SlotTakes(factory, slots, slot, part))
                            failures.Add($"{vehicle.ID}: slot {slot.Id} takes {part.ID}, a gun for a vehicle with no weapons seat.");
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The slot rule of the hardpoint system (family, slot type, compatibility id, type, whitelist) on prototype data.</summary>
    private static bool SlotTakes(IComponentFactory factory, HardpointSlotsComponent slots, HardpointSlot slot, EntityPrototype part)
    {
        if (!part.TryGetComponent<HardpointItemComponent>(out var item, factory))
            return false;

        if (slots.VehicleFamily != null && item.VehicleFamily != slots.VehicleFamily)
            return false;

        if (slot.SlotType != null && item.SlotType != slot.SlotType)
            return false;

        if (item.CompatibilityId != slot.CompatibilityId)
            return false;

        if (slot.HardpointType != default && item.HardpointType != slot.HardpointType)
            return false;

        return WhitelistTakes(factory, slot.Whitelist, part);
    }

    /// <summary>An entity whitelist on prototype data; a size rule is left unjudged, which counts as passing.</summary>
    private static bool WhitelistTakes(IComponentFactory factory, EntityWhitelist? whitelist, EntityPrototype part)
    {
        if (whitelist == null)
            return true;

        var components = whitelist.Components ?? System.Array.Empty<string>();
        var tags = whitelist.Tags ?? new List<ProtoId<TagPrototype>>();
        if (components.Length == 0 && tags.Count == 0)
            return true;

        var partTags = part.TryGetComponent<TagComponent>(out var tagComponent, factory) ? tagComponent.Tags : null;
        var matches = components.Select(name => part.Components.ContainsKey(name))
            .Concat(tags.Select(tag => partTags != null && partTags.Contains(tag)))
            .ToList();

        return whitelist.RequireAll ? matches.All(m => m) : matches.Any(m => m);
    }

    /// <summary>The port guns start with a magazine whose rounds leave no casing, in the slot the port prop names.</summary>
    [Test]
    public async Task ThePortGunsStartLoadedWithCasinglessRounds()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var portGuns = protos.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && !p.ID.Contains("Test") && p.TryGetComponent<VehiclePortGunComponent>(out _, factory))
                .ToList();
            Assert.That(portGuns.Select(p => p.ID), Is.EquivalentTo(new[] { "VehicleAPCM56FPWPortGunLeft", "VehicleAPCM56FPWPortGunRight" }));

            foreach (var gun in portGuns)
            {
                Assert.That(gun.TryGetComponent<ItemSlotsComponent>(out var slots, factory), Is.True, $"{gun.ID}: no item slots.");
                var slotMap = slots!.Slots;
                Assert.That(slotMap.TryGetValue("gun_magazine", out var slot), Is.True, $"{gun.ID}: no magazine slot.");
                Assert.That(slot!.StartingItem, Is.Not.Null, $"{gun.ID}: no starting magazine.");

                var magazine = protos.Index<EntityPrototype>(slot.StartingItem!);
                Assert.That(magazine.TryGetComponent<BallisticAmmoProviderComponent>(out var ammo, factory), Is.True, $"{gun.ID}: the magazine has no rounds.");
                CheckCartridge(protos, factory, gun.ID, ammo!.Proto!.Value);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The guns in a starting item and, for a turret body, in the slots it starts with.</summary>
    private static int StartingGuns(IPrototypeManager protos, IComponentFactory factory, string? startingItem)
    {
        if (startingItem == null || !protos.TryIndex<EntityPrototype>(startingItem, out var item))
            return 0;

        var count = item.TryGetComponent<HardpointItemComponent>(out _, factory) && item.TryGetComponent<GunComponent>(out _, factory) ? 1 : 0;
        if (item.TryGetComponent<ItemSlotsComponent>(out var slots, factory))
        {
            foreach (var slot in slots.Slots.Values)
            {
                count += StartingGuns(protos, factory, slot.StartingItem);
            }
        }

        return count;
    }

    /// <summary>
    /// A pre-armed variant keeps every slot of the vehicle it is made from and starts with guns mounted. Two parents are not merged:
    /// the first one's slots win, so a loadout listed after its vehicle is dropped and the variant spawns as bare as the vehicle.
    /// </summary>
    [Test]
    public async Task EveryArmedVariantKeepsTheSlotsOfItsVehicleAndStartsWithGuns()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var variants = protos.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && p.ID.Contains("Armed") && p.TryGetComponent<RMCVehicleComponent>(out _, factory))
                .OrderBy(p => p.ID)
                .ToList();

            Assert.That(variants, Has.Count.GreaterThanOrEqualTo(8), "The armed variants are missing.");

            var failures = new List<string>();
            foreach (var variant in variants)
            {
                if (!variant.TryGetComponent<ItemSlotsComponent>(out var variantSlots, factory))
                {
                    failures.Add($"{variant.ID}: no item slots.");
                    continue;
                }

                var variantMap = variantSlots.Slots;
                var guns = variantMap.Values.Sum(slot => StartingGuns(protos, factory, slot.StartingItem));
                if (guns == 0)
                    failures.Add($"{variant.ID}: it starts with no gun mounted.");

                // The vehicle it is made from: its nearest parent that is not abstract (a loadout is).
                var baseId = protos.EnumerateParents<EntityPrototype>(variant.ID).FirstOrDefault()?.ID;
                if (baseId == null ||
                    !protos.Index<EntityPrototype>(baseId).TryGetComponent<ItemSlotsComponent>(out var baseSlots, factory))
                {
                    failures.Add($"{variant.ID}: no vehicle to compare it with.");
                    continue;
                }

                var baseMap = baseSlots.Slots;
                foreach (var slotId in baseMap.Keys)
                {
                    if (!variantMap.ContainsKey(slotId))
                        failures.Add($"{variant.ID}: the slot {slotId} of {baseId} is gone.");
                }

                var baseGuns = baseMap.Values.Sum(slot => StartingGuns(protos, factory, slot.StartingItem));
                if (guns < baseGuns)
                    failures.Add($"{variant.ID}: it starts with {guns} guns, fewer than the {baseGuns} of {baseId}.");

                // What the variant adds to its vehicle shows in a slot that starts with something else.
                if (variantMap.Count(pair => !baseMap.TryGetValue(pair.Key, out var other) || other.StartingItem != pair.Value.StartingItem) == 0)
                    failures.Add($"{variant.ID}: it starts with exactly what {baseId} starts with, so nothing is armed.");
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        });

        await pair.CleanReturnAsync();
    }

    // The speed a gun's source gives its rounds. RMC and CMU set one on these guns only; every other gun of the roster leaves it at
    // the engine default of its source, 53 for RMC. A gun inherits the value of its nearest parent in the table.
    private static readonly Dictionary<string, float> SourceSpeeds = new()
    {
        // RMC
        ["VehicleTankAceAutocannon"] = 50f,
        ["VehicleTankGrenadeLauncher"] = 18f,
        ["VehicleTankTowLauncher"] = 18f,
        ["VehicleTankRocketLauncher"] = 18f,
        ["VehicleTankFlareModule"] = 20f,
        ["RMCAPCFlareLauncher"] = 20f,
        ["VehicleHumveeLauncher"] = 18f,

        // CMU: its engine default is 62
        // CMU flies its flame jets at 20. They are kept at RMC's default here: see Hardpoints/tank_weapons.yml.
        ["VehicleTankDragonFlamer"] = 53f,
        ["VehicleTankFlamer"] = 53f,
        ["VehicleTankAutocannonTWE"] = 50f,
        ["VehicleTankHeavyElectrolaserTWE"] = 60f,
        ["VehicleTankCupolaTWE"] = 62f,
        ["VehicleTankRocketLauncherTWE"] = 18f,
        ["VehicleTankRERE700"] = 62f,
    };

    private const float RmcDefaultSpeed = 53f;

    /// <summary>
    /// The owner kept the source's weapon numbers: every gun flies its rounds at the speed its source gave, not at Wolfgate's own
    /// default of 25.
    /// </summary>
    [Test]
    public async Task EveryGunFliesAtItsSourceSpeed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var failures = new List<string>();
            var checkedGuns = 0;

            foreach (var gun in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (gun.Abstract || gun.ID.Contains("Test") ||
                    !gun.TryGetComponent<HardpointItemComponent>(out _, factory) ||
                    !gun.TryGetComponent<GunComponent>(out var gunComp, factory))
                {
                    continue;
                }

                var expected = RmcDefaultSpeed;
                foreach (var parent in protos.EnumerateParents<EntityPrototype>(gun.ID, true))
                {
                    if (SourceSpeeds.TryGetValue(parent.ID, out var speed))
                    {
                        expected = speed;
                        break;
                    }
                }

                checkedGuns++;
                if (gunComp.ProjectileSpeed != expected)
                    failures.Add($"{gun.ID}: its rounds fly at {gunComp.ProjectileSpeed}, the source's is {expected}.");
            }

            Assert.That(checkedGuns, Is.GreaterThanOrEqualTo(25), "The armed hardpoints are missing.");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        });

        await pair.CleanReturnAsync();
    }

    // The tiles at which the source cut a kinetic round's damage off, by round. A round inherits the range of its nearest parent here.
    private static readonly Dictionary<string, float> SourceRanges = new()
    {
        ["VehicleBulletSentry10x50mm"] = 12f,
        ["VehicleBulletLTAAAP"] = 22f,
        ["RMCProjectileAceAutocannonFlak"] = 32f,
        ["RMCProjectileElectrolaser"] = 32f,
    };

    /// <summary>
    /// A kinetic round that has a range limit flies the source's range at its gun's speed, and one without a limit in the source has none.
    /// A shell that goes to a point (the fixed-distance guns) takes its range from the aim and has a lifetime of its own.
    /// </summary>
    [Test]
    public async Task KineticRoundsAreCutOffAtTheirSourceRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            var failures = new List<string>();
            var seenRanges = new HashSet<string>();

            foreach (var gun in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (gun.Abstract || gun.ID.Contains("Test") ||
                    !gun.TryGetComponent<HardpointItemComponent>(out _, factory) ||
                    !gun.TryGetComponent<GunComponent>(out var gunComp, factory) ||
                    gun.TryGetComponent<ShootAtFixedPointComponent>(out _, factory) ||
                    !gun.TryGetComponent<BallisticAmmoProviderComponent>(out var ammo, factory) ||
                    ammo.Proto is not { } cartridgeId ||
                    !protos.Index<EntityPrototype>(cartridgeId).TryGetComponent<CartridgeAmmoComponent>(out var cartridge, factory))
                {
                    continue;
                }

                var round = protos.Index<EntityPrototype>(cartridge.Prototype);

                float? expected = null;
                foreach (var parent in protos.EnumerateParents<EntityPrototype>(round.ID, true))
                {
                    if (SourceRanges.TryGetValue(parent.ID, out var range))
                    {
                        expected = range;
                        seenRanges.Add(parent.ID);
                        break;
                    }
                }

                // BaseBullet lives ten seconds, which is no range limit; a shorter lifetime is one.
                var limited = round.TryGetComponent<TimedDespawnComponent>(out var despawn, factory) && despawn.Lifetime < 5f;
                if (expected == null)
                {
                    if (limited)
                        failures.Add($"{gun.ID}: its round {round.ID} is cut off after {despawn!.Lifetime} seconds, which the source does not do.");

                    continue;
                }

                if (!limited)
                {
                    failures.Add($"{gun.ID}: its round {round.ID} has no range limit, the source cuts it off at {expected} tiles.");
                    continue;
                }

                var flown = despawn!.Lifetime * gunComp.ProjectileSpeed;
                if (Math.Abs(flown - expected.Value) > 0.5f)
                    failures.Add($"{gun.ID}: its round {round.ID} flies {flown:0.0} tiles, the source's range is {expected}.");
            }

            foreach (var id in SourceRanges.Keys)
            {
                Assert.That(seenRanges, Does.Contain(id), $"No gun fires {id}.");
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The heavy rounds keep the source's numbers: the LTB cannon shell's blast type and its multiplier against vehicles, the TOW rocket's,
    /// the smoke shell's range, and the CMU sentry round of the hull machine gun and the RE700, which is not RMC's.
    /// </summary>
    [Test]
    public async Task TheHeavyRoundsKeepTheirSourceNumbers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.EntMan.ComponentFactory;

        await server.WaitAssertion(() =>
        {
            EntityPrototype Round(string id) => protos.Index<EntityPrototype>(id);
            T Get<T>(EntityPrototype proto) where T : IComponent, new()
            {
                Assert.That(proto.TryGetComponent<T>(out var found, factory), Is.True, $"{proto.ID} has no {typeof(T).Name}.");
                return found!;
            }

            using (Assert.EnterMultipleScope())
            {
                // The LTB cannon shell: RMC's intensity numbers and its own explosion type.
                var shell = Round("RMCProjectileLTBCannon");
                var shellBlast = Get<ExplosiveComponent>(shell);
                Assert.That(shellBlast.ExplosionType.ToString(), Is.EqualTo("RMCLTBCannon"));
                Assert.That((shellBlast.MaxIntensity, shellBlast.IntensitySlope, shellBlast.TotalIntensity), Is.EqualTo((40f, 18f, 650f)));
                Assert.That((shellBlast.TileBreakScale, shellBlast.MaxTileBreak), Is.EqualTo((4f, 12)));
                Assert.That(Get<VehicleDamageMultiplierComponent>(shell).Multiplier, Is.EqualTo(10f));
                Assert.That(Get<Content.Server.Explosion.Components.TriggerOnCollideComponent>(shell).IgnoreOtherNonHard, Is.True,
                    "The shell must not go off on the non-hard fly-by sensor of another bullet.");

                var ltb = protos.Index(shellBlast.ExplosionType);
                Assert.That(ltb.DamagePerIntensity.DamageDict["Blunt"].Float(), Is.EqualTo(20f));
                Assert.That(ltb.DamagePerIntensity.DamageDict["Heat"].Float(), Is.EqualTo(20f));
                Assert.That(ltb.DamagePerIntensity.DamageDict["Structural"].Float(), Is.EqualTo(400f));

                // The TOW rocket.
                var tow = Round("VehicleProjectileRocket84mmAntiArmor");
                var towBlast = Get<ExplosiveComponent>(tow);
                Assert.That(towBlast.ExplosionType.ToString(), Is.EqualTo("RMC"));
                Assert.That((towBlast.MaxIntensity, towBlast.IntensitySlope, towBlast.TotalIntensity), Is.EqualTo((10f, 5f, 35f)));
                Assert.That(Get<VehicleDamageMultiplierComponent>(tow).Multiplier, Is.EqualTo(5f));

                var rmc = protos.Index(towBlast.ExplosionType);
                Assert.That(rmc.DamagePerIntensity.DamageDict["Blunt"].Float(), Is.EqualTo(5f));
                Assert.That(rmc.DamagePerIntensity.DamageDict["Heat"].Float(), Is.EqualTo(5f));
                Assert.That(rmc.DamagePerIntensity.DamageDict.ContainsKey("Structural"), Is.False);

                // The smoke shell goes no further than 9 tiles, in RMC and CMU alike.
                Assert.That(Get<ProjectileMaxFixedRangeComponent>(Round("WFVehicleSmokeShell")).MaxFixedRange, Is.EqualTo(9f));
            }

            // The sentry round: RMC's cupola fires RMC's 30 damage round, CMU's hull machine gun and RE700 CMU's 20 damage round.
            float Damage(string gunId)
            {
                var gun = protos.Index<EntityPrototype>(gunId);
                var ammo = Get<BallisticAmmoProviderComponent>(gun);
                var cartridge = Get<CartridgeAmmoComponent>(protos.Index<EntityPrototype>(ammo.Proto!.Value));
                return Get<ProjectileComponent>(Round(cartridge.Prototype)).Damage.GetTotal().Float();
            }

            bool Limited(string gunId)
            {
                var gun = protos.Index<EntityPrototype>(gunId);
                var ammo = Get<BallisticAmmoProviderComponent>(gun);
                var cartridge = Get<CartridgeAmmoComponent>(protos.Index<EntityPrototype>(ammo.Proto!.Value));
                var round = Round(cartridge.Prototype);
                return round.TryGetComponent<TimedDespawnComponent>(out var despawn, factory) && despawn.Lifetime < 5f;
            }

            Assert.That(Damage("VehicleTankM56Cupola"), Is.EqualTo(30f));
            Assert.That(Limited("VehicleTankM56Cupola"), Is.True);
            foreach (var cmuGun in new[] { "VehicleTankCupolaTWE", "VehicleTankRERE700" })
            {
                Assert.That(Damage(cmuGun), Is.EqualTo(20f), $"{cmuGun}: CMU's sentry round does 20 damage.");
                Assert.That(Limited(cmuGun), Is.False, $"{cmuGun}: CMU's sentry round has no range cut-off.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
