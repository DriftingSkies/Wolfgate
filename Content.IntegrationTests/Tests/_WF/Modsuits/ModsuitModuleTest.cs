using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Modsuits;
using Content.Client.Overlays;
using Content.Server.Medical.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._WF.Modsuits;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Armor;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Movement.Components;
using Content.Shared.Players;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Prototypes;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Wires;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests._WF.Modsuits;

/// <summary>Checks a MODsuit deploys, seals, takes modules through the native module system, and stays within its slowdown cap.</summary>
[TestFixture]
public sealed class ModsuitModuleTest
{
    [Test]
    public async Task SuitDeploysSealsAndAcceptsModule()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var inventory = entMan.System<InventorySystem>();
        var wires = entMan.System<SharedWiresSystem>();
        var batteries = entMan.System<SharedBatterySystem>();
        EntityUid human = default;
        EntityUid suit = default;
        EntityUid module = default;
        EntityUid flashlight = default;
        EntityUid jetpack = default;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            human = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", coords);
            module = entMan.SpawnEntity("ModsuitModuleStorageCompact", coords);
            flashlight = entMan.SpawnEntity("ModsuitModuleFlashlight", coords);
            jetpack = entMan.SpawnEntity("ModsuitModuleJetpack", coords);

            Assert.That(inventory.TryEquip(human, suit, "back", silent: true, force: true));
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<ModSuitComponent>(suit), "the control unit should use the native module system");
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.Parts, Has.Count.EqualTo(4), $"the prototype should name four parts, got {comp.Parts.Count}");
            Assert.That(comp.PartEntities, Has.Count.EqualTo(4), $"the suit should spawn its four parts, got {comp.PartEntities.Count}");
            var battery = modSuit.GetBattery((suit, comp));
            Assert.That(battery, Is.Not.Null, "the standard core should carry a power cell");
            Assert.That(modSuit.GetCharge(battery!.Value), Is.GreaterThan(0f), "the core cell should start charged");
            var core = modSuit.GetCore((suit, comp));
            Assert.That(core, Is.Not.Null);
            Assert.That(comp.ModulesContainer.ContainedEntities, Has.Count.EqualTo(3), "the power-cell suit should ship with its three basic modules");

            var panel = entMan.GetComponent<WiresPanelComponent>(suit);
            Assert.That(wires.TogglePanel(suit, panel, true, human));

            Assert.That(modSuit.TryInstallModule((suit, comp), module, human), Is.False,
                "a second storage module should be rejected as a duplicate");
            Assert.That(modSuit.TryInstallModule((suit, comp), flashlight, human), Is.False,
                "a second flashlight should be rejected as a duplicate");
            Assert.That(modSuit.TryInstallModule((suit, comp), jetpack, human), "a jetpack module should install");

            Assert.That(modSuit.GetComplexity((suit, comp)), Is.EqualTo(6));
            Assert.That(entMan.HasComponent<StorageComponent>(suit), Is.True,
                "the compact storage module should grant storage as soon as it is installed");
            Assert.That(wires.TogglePanel(suit, panel, false, human), "the panel should close before sealing");

            Assert.That(comp.Wearer, Is.EqualTo(human), "the suit should know its wearer");
            modSuit.ToggleAllParts((suit, comp), human);
            Assert.That(comp.DeployQueue, Is.Not.Empty, "deploying should queue the parts");
        });

        await server.WaitRunTicks(120);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.AllPartsDeployed((suit, comp)), "all four parts should be deployed");
            Assert.That(modSuit.TryToggleActivation((suit, comp), human));
        });

        await server.WaitRunTicks(360);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Active), "the suit should finish sealing");
            Assert.That(modSuit.TryUseModule(jetpack, human), "the ion jetpack should switch on in weightless space");
            Assert.That(entMan.HasComponent<ActiveJetpackComponent>(jetpack), "the jetpack should be active");
            var jetpackComp = entMan.GetComponent<JetpackComponent>(jetpack);
            Assert.That(jetpackComp.JetpackUser, Is.EqualTo(human),
                "the jetpack should track its wearer so the client can draw particles");
            Assert.That(jetpackComp.Acceleration, Is.GreaterThan(1f), "the ion jetpack acceleration should match a normal jetpack");
            Assert.That(jetpackComp.WeightlessModifier, Is.GreaterThan(2f), "the ion jetpack should not move at half speed");
            var coreBattery = modSuit.GetBattery((suit, comp))!.Value;
            batteries.ChangeCharge(coreBattery.Owner, -100000f);

            EntityUid? installedFlashlight = null;
            foreach (var installed in comp.ModulesContainer.ContainedEntities)
            {
                if (entMan.GetComponent<MetaDataComponent>(installed).EntityPrototype?.ID == "ModsuitModuleFlashlight")
                {
                    installedFlashlight = installed;
                    break;
                }
            }

            Assert.That(installedFlashlight, Is.Not.Null);
            Assert.That(modSuit.TryUseModule(installedFlashlight!.Value, human), Is.False,
                "a powered module must not run on an empty core");
            foreach (var part in comp.PartEntities.Values)
            {
                Assert.That(entMan.GetComponent<ModPartComponent>(part).Sealed,
                    $"every deployed part should be sealed; part {entMan.ToPrettyString(part)} failed");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StandardSuitShipsReadyToUse()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);

            var suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", new MapCoordinates(0, 0, mapId));
            var comp = entMan.GetComponent<ModSuitComponent>(suit);

            var battery = modSuit.GetBattery((suit, comp));
            Assert.That(battery, Is.Not.Null, "the standard core should ship with a power cell");
            Assert.That(modSuit.GetCharge(battery!.Value), Is.GreaterThan(0f), "the shipped cell should be charged");
            Assert.That(comp.ModulesContainer.ContainedEntities, Has.Count.EqualTo(3),
                "the power-cell suit should ship with storage, welding and a flashlight");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoreAndCellAreItemSlots()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var wires = entMan.System<SharedWiresSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();

        EntityUid human = default;
        EntityUid suit = default;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            human = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", coords);
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            var core = modSuit.GetCore((suit, comp));
            Assert.That(core, Is.Not.Null, "the standard suit should ship with a core");

            Assert.That(itemSlots.TryEject(suit, comp.CoreSlotId, human, out _), Is.False,
                "the sealed maintenance panel should block ejecting the core");
            Assert.That(modSuit.GetCore((suit, comp)), Is.Not.Null);

            var panel = entMan.GetComponent<WiresPanelComponent>(suit);
            Assert.That(wires.TogglePanel(suit, panel, true, human));
            Assert.That(itemSlots.TryEject(suit, comp.CoreSlotId, human, out var ejected), Is.True,
                "the core should eject with the panel open");
            Assert.That(ejected, Is.EqualTo(core));
            Assert.That(modSuit.GetCore((suit, comp)), Is.Null);

            Assert.That(itemSlots.TryEject(core.Value, "cell_slot", human, out var cell), Is.True,
                "the cell should come out of a loose core");
            Assert.That(cell, Is.Not.Null);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CoreRejectsMediumAndHighPowerCages()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var itemSlots = entMan.System<ItemSlotsSystem>();

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            var core = entMan.SpawnEntity("ModsuitCoreStandard", coords);
            var small = entMan.SpawnEntity("PowerCageSmall", coords);
            Assert.That(itemSlots.TryInsert(core, "cell_slot", small, null), Is.True,
                "a small power cage should fit a MODsuit core");
            Assert.That(itemSlots.TryEject(core, "cell_slot", null, out _), Is.True);

            var medium = entMan.SpawnEntity("PowerCageMedium", coords);
            Assert.That(itemSlots.TryInsert(core, "cell_slot", medium, null), Is.False,
                "a medium power cage gives a MODsuit core too much energy");

            var high = entMan.SpawnEntity("PowerCageHigh", coords);
            Assert.That(itemSlots.TryInsert(core, "cell_slot", high, null), Is.False,
                "a high power cage gives a MODsuit core too much energy");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovingStorageModuleDropsContentsAndRemovesStorage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var storage = entMan.System<SharedStorageSystem>();
        var wires = entMan.System<SharedWiresSystem>();

        EntityUid human = default;
        EntityUid suit = default;
        EntityUid module = default;
        var items = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            human = entMan.SpawnEntity("MobHuman", coords);
            suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", coords);

            var suitComp = entMan.GetComponent<ModSuitComponent>(suit);
            var panel = entMan.GetComponent<WiresPanelComponent>(suit);
            Assert.That(wires.TogglePanel(suit, panel, true, human));

            module = suitComp.ModulesContainer.ContainedEntities.First(m =>
                entMan.GetComponent<MetaDataComponent>(m).EntityPrototype?.ID == "ModsuitModuleStorageCompact");
            Assert.That(entMan.TryGetComponent<StorageComponent>(suit, out var suitStorage), Is.True);

            foreach (var proto in new[] { "Wrench", "Screwdriver" })
            {
                var item = entMan.SpawnEntity(proto, coords);
                items.Add(item);
                Assert.That(storage.Insert(suit, item, out _, user: human, storageComp: suitStorage, playSound: false),
                    Is.True, $"{proto} should fit in the suit storage");
            }

            Assert.That(modSuit.TryRemoveModule((suit, suitComp), module, human), Is.True,
                "the storage module should be removable with the panel open");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<StorageComponent>(suit), Is.False,
                "removing the storage module should remove the suit's storage");

            foreach (var item in items)
            {
                Assert.That(entMan.EntityExists(item), Is.True, "the stored item should still exist");
                Assert.That(entMan.GetComponent<TransformComponent>(item).ParentUid,
                    Is.Not.EqualTo(suit), "the stored item should be dropped out of the suit");
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryModSuitNamesItsParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var prototypes = client.ResolveDependency<IPrototypeManager>();
            var factory = client.ResolveDependency<IComponentFactory>();

            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryComp<ModSuitComponent>(out var suit, factory))
                    continue;

                Assert.That(suit.Parts, Has.Count.EqualTo(4),
                    $"{proto.ID} should name four suit parts");

                foreach (var (slot, partId) in suit.Parts)
                {
                    Assert.That(prototypes.TryIndex<EntityPrototype>(partId, out var part), Is.True,
                        $"{proto.ID} names missing part {partId} for {slot}");
                    Assert.That(part!.TryComp<ModSuitSealedVisualsComponent>(out var visuals, factory), Is.True,
                        $"{partId} has no ModSuitSealedVisuals component for its deployed state");
                    Assert.That(visuals!.ClothingVisuals.ContainsKey(slot), Is.True,
                        $"{partId} has no sealed visual for the {slot} slot");
                }
            }
        });

        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var modSuit = entMan.System<ModSuitSystem>();
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            foreach (var id in new[]
                     {
                         "ClothingModsuitUSSPVaryagPowerCell",
                         "ClothingModsuitVoltaPowerCell",
                         "ADTClothingModsuitBackClownPowerCell",
                     })
            {
                var suit = entMan.SpawnEntity(id, coords);
                var comp = entMan.GetComponent<ModSuitComponent>(suit);

                Assert.That(comp.Parts, Has.Count.EqualTo(4), $"{id} should name four suit parts");
                Assert.That(comp.PartEntities, Has.Count.EqualTo(4), $"{id} should spawn its four suit parts");

                var core = modSuit.GetCore((suit, comp));
                Assert.That(core, Is.Not.Null, $"{id} should ship with a core");
                Assert.That(entMan.HasComponent<ModCoreComponent>(core!.Value), Is.True,
                    $"{id} should use a MOD core");
                Assert.That(modSuit.GetBattery((suit, comp)), Is.Not.Null, $"{id} should ship with a charged cell");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The RX-01 parts only name sprite states that exist in their RSIs.</summary>
    [Test]
    public async Task RogueModSuitPartsHaveAllSpriteStates()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var prototypes = client.ResolveDependency<IPrototypeManager>();
        var factory = client.ResolveDependency<IComponentFactory>();

        await client.WaitAssertion(() =>
        {
            foreach (var id in new[]
                     {
                         "ClothingModsuitHelmetRogue",
                         "ClothingModsuitChestplateRogue",
                         "ClothingModsuitGauntletsRogue",
                         "ClothingModsuitBootsRogue",
                     })
            {
                var proto = prototypes.Index<EntityPrototype>(id);
                Assert.That(proto.TryGetComponent<SpriteComponent>(out var sprite, factory), Is.True,
                    $"{proto.ID} has no sprite");
                var rsi = sprite!.BaseRSI;
                Assert.That(rsi, Is.Not.Null, $"{proto.ID} has no RSI");

                if (proto.TryGetComponent<ModSuitSealedVisualsComponent>(out var sealedVisuals, factory))
                {
                    foreach (var layer in sealedVisuals!.ClothingVisuals.Values.SelectMany(l => l))
                    {
                        if (layer.RsiPath != null || layer.State is not { } state)
                            continue;

                        Assert.That(rsi!.TryGetState(state, out _), Is.True,
                            $"{proto.ID} names missing sealed sprite state {state} in {rsi.Path}");
                    }
                }

                if (proto.TryGetComponent<ClothingComponent>(out var clothing, factory))
                {
                    foreach (var layer in clothing!.ClothingVisuals.Values.SelectMany(l => l))
                    {
                        if (layer.RsiPath != null || layer.State is not { } state)
                            continue;

                        Assert.That(rsi!.TryGetState(state, out _), Is.True,
                            $"{proto.ID} names missing clothing sprite state {state} in {rsi.Path}");
                    }
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryPortedModuleHasAComponent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.ID.StartsWith("ModsuitModule", StringComparison.Ordinal))
                    continue;

                Assert.That(proto.TryComp<ModModuleComponent>(out _, factory), Is.True,
                    $"{proto.ID} is a module prototype but has no ModModule component");
                Assert.DoesNotThrow(() => entMan.SpawnEntity(proto.ID, coords),
                    $"{proto.ID} should initialise");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every deployed suit stays at 10% slowdown at most, or 5% when its piercing protection is low.</summary>
    [Test]
    public async Task EveryModSuitStaysUnderItsSlowdownCap()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryComp<ModSuitComponent>(out var suit, factory))
                    continue;

                var parts = suit.Parts;
                if (!parts.ContainsKey("outerClothing")
                    || !prototypes.TryIndex(parts["outerClothing"], out var chest))
                    continue;

                var piercing = 1f;
                if (chest.TryComp<ArmorComponent>(out var armor, factory)
                    && armor.Modifiers.Coefficients.TryGetValue("Piercing", out var value))
                    piercing = value;

                var cap = piercing <= 0.6f ? 0.10f : 0.05f;
                var walk = 1f;
                var sprint = 1f;

                foreach (var partId in parts.Values)
                {
                    if (!prototypes.TryIndex(partId, out var part)
                        || !part.TryComp<ClothingSpeedModifierComponent>(out var speed, factory))
                        continue;

                    walk *= speed.WalkModifier;
                    sprint *= speed.SprintModifier;
                }

                if (proto.TryComp<ClothingSpeedModifierComponent>(out var suitSpeed, factory))
                {
                    walk *= suitSpeed.WalkModifier;
                    sprint *= suitSpeed.SprintModifier;
                }

                Assert.Multiple(() =>
                {
                    Assert.That(1f - walk, Is.LessThanOrEqualTo(cap + 0.0001f),
                        $"{proto.ID} has {1f - walk:P2} walk slowdown, cap is {cap:P0}");
                    Assert.That(1f - sprint, Is.LessThanOrEqualTo(cap + 0.0001f),
                        $"{proto.ID} has {1f - sprint:P2} sprint slowdown, cap is {cap:P0}");
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A worn suit's core charges while its wearer is inside a cyborg recharger.</summary>
    [Test]
    public async Task SuitChargesInsideBorgCharger()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var batteries = entMan.System<BatterySystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var inventory = entMan.System<InventorySystem>();
        var modSuit = entMan.System<ModSuitSystem>();

        EntityUid wearer = default, suit = default;
        await server.WaitPost(() =>
        {
            wearer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            suit = entMan.SpawnEntity("ClothingModsuitStandardPowerCell", map.GridCoords);
            Assert.That(inventory.TryEquip(wearer, suit, "back", force: true), Is.True);

            var suitComp = entMan.GetComponent<ModSuitComponent>(suit);
            var battery = modSuit.GetBattery((suit, suitComp));
            Assert.That(battery, Is.Not.Null);
            batteries.SetCharge(battery!.Value.Owner, 0f, battery.Value.Comp);

            var charger = entMan.SpawnEntity("BorgCharger", map.GridCoords);
            Assert.That(entMan.GetComponent<TransformComponent>(charger).Anchored, Is.True,
                "the cyborg recharger should spawn anchored");
            var receiver = entMan.EnsureComponent<ApcPowerReceiverComponent>(charger);
            receiver.NeedsPower = false;
            receiver.Powered = true;

            Assert.That(containers.TryGetContainer(charger, "entity_storage", out var container), Is.True);
            Assert.That(containers.Insert(wearer, container!), Is.True);
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var suitComp = entMan.GetComponent<ModSuitComponent>(suit);
            var battery = modSuit.GetBattery((suit, suitComp));
            Assert.That(battery, Is.Not.Null);
            Assert.That(battery!.Value.Comp.CurrentCharge, Is.GreaterThan(0f),
                "the suit core was not charged by the cyborg recharger");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A suit can start with some parts retracted, but modules only work while their part is deployed.</summary>
    [Test]
    public async Task SuitActivatesWithOnlyDeployedParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var inventory = entMan.System<InventorySystem>();
        var modSuit = entMan.System<ModSuitSystem>();

        EntityUid wearer = default, suit = default;
        await server.WaitPost(() =>
        {
            wearer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            suit = entMan.SpawnEntity("ClothingModsuitEngineerPowerCell", map.GridCoords);
            Assert.That(inventory.TryEquip(wearer, suit, "back", force: true), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.TryStartActivation((suit, comp), wearer), Is.True,
                "the suit refused to activate with every part retracted");
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Active));
            foreach (var (slot, part) in comp.PartEntities)
            {
                Assert.That(entMan.GetComponent<ModPartComponent>(part).Sealed, Is.False,
                    $"{slot} sealed while it was retracted");
            }

            modSuit.StartDeactivation((suit, comp));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(6f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Inactive));
            Assert.That(modSuit.TryDeployPart((suit, comp), "head", wearer), Is.True);
            Assert.That(modSuit.TryStartActivation((suit, comp), wearer), Is.True);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Active));
            Assert.That(entMan.GetComponent<ModPartComponent>(comp.PartEntities["head"]).Sealed, Is.True,
                "the deployed helmet did not seal");
            Assert.That(entMan.GetComponent<ModPartComponent>(comp.PartEntities["shoes"]).Sealed, Is.False,
                "the retracted boots sealed");

            EntityUid? flashlight = null;
            EntityUid? magboots = null;
            foreach (var module in comp.ModulesContainer.ContainedEntities)
            {
                switch (entMan.GetComponent<MetaDataComponent>(module).EntityPrototype?.ID)
                {
                    case "ModsuitModuleFlashlight":
                        flashlight = module;
                        break;
                    case "ModsuitModuleMagboots":
                        magboots = module;
                        break;
                }
            }

            Assert.That(flashlight, Is.Not.Null);
            Assert.That(magboots, Is.Not.Null);
            Assert.That(modSuit.TryUseModule(flashlight!.Value, wearer), Is.True,
                "a module whose part is deployed should still work");
            Assert.That(modSuit.TryUseModule(magboots!.Value, wearer), Is.False,
                "a module whose part is retracted should not work");

            Assert.That(modSuit.TryDeployPart((suit, comp), "shoes", wearer), Is.True,
                "the suit should deploy a part while active");
            Assert.That(entMan.GetComponent<ModPartComponent>(comp.PartEntities["shoes"]).Sealed, Is.True,
                "a part deployed while active should seal immediately");
            Assert.That(modSuit.TryUseModule(magboots.Value, wearer), Is.True,
                "the module should work once its part is deployed");
            modSuit.ToggleAllParts((suit, comp), wearer);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Active));
            foreach (var (slot, part) in comp.PartEntities)
            {
                Assert.That(entMan.GetComponent<ModPartComponent>(part).Sealed, Is.True,
                    $"{slot} should be deployed and sealed");
            }

            Assert.That(modSuit.TryRetractPart((suit, comp), "shoes", wearer), Is.True,
                "the suit should retract a part while active");
            Assert.That(modSuit.IsPartDeployed((suit, comp), "shoes"), Is.False);
            Assert.That(entMan.GetComponent<ModPartComponent>(comp.PartEntities["shoes"]).Sealed, Is.False,
                "a part retracted while active should unseal");

            EntityUid? bootModule = null;
            foreach (var module in comp.ModulesContainer.ContainedEntities)
            {
                if (entMan.GetComponent<MetaDataComponent>(module).EntityPrototype?.ID == "ModsuitModuleMagboots")
                {
                    bootModule = module;
                    break;
                }
            }

            Assert.That(bootModule, Is.Not.Null);
            Assert.That(entMan.GetComponent<ModModuleComponent>(bootModule!.Value).Active, Is.False,
                "retracting the boots should shut off modules that need them");

            Assert.That(modSuit.TryDeployPart((suit, comp), "shoes", wearer), Is.True);
            modSuit.ToggleAllParts((suit, comp), wearer);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Active),
                "retracting parts should not turn the suit off");
            foreach (var (slot, part) in comp.PartEntities)
            {
                Assert.That(modSuit.IsPartDeployed((suit, comp), slot), Is.False,
                    $"{slot} should be retracted");
                Assert.That(entMan.GetComponent<ModPartComponent>(part).Sealed, Is.False,
                    $"{slot} should be unsealed");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every ready suit installs every module its prototype and theme list.</summary>
    [Test]
    public async Task EveryModSuitInstallsItsStartingModules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            maps.CreateMap(out var mapId);
            var coords = new MapCoordinates(0, 0, mapId);

            foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryComp<ModSuitComponent>(out var suit, factory))
                    continue;

                var theme = prototypes.Index(suit.Theme);
                var expected = suit.StartingModules.Count + suit.InbuiltModules.Count + theme.InbuiltModules.Count;
                var suitEntity = entMan.SpawnEntity(proto.ID, coords);
                var comp = entMan.GetComponent<ModSuitComponent>(suitEntity);
                Assert.That(comp.ModulesContainer.ContainedEntities.Count, Is.EqualTo(expected),
                    $"{proto.ID} installed {comp.ModulesContainer.ContainedEntities.Count} of {expected} modules");
                entMan.DeleteEntity(suitEntity);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Mono suits leave storage to their modules so a swapped module changes the space.</summary>
    [Test]
    public async Task MonoSuitsUseStorageModules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            foreach (var id in new[]
                     {
                         "ClothingModsuitRogue",
                         "ClothingModsuitVolta",
                         "ClothingModsuitMercenary",
                     })
            {
                var proto = prototypes.Index<EntityPrototype>(id);
                Assert.That(proto.TryComp<StorageComponent>(out _, factory), Is.False,
                    $"{id} hard-codes Storage, which blocks its storage module");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Examining a MOD shell names its build steps instead of showing raw loc keys.</summary>
    [Test]
    public async Task ModsuitShellExamineUsesLocalizedBuildSteps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid shell = default;
        EntityUid viewer = default;

        await server.WaitPost(() =>
        {
            viewer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            shell = entMan.SpawnEntity("ModsuitShell", map.GridCoords);
        });

        await server.WaitAssertion(() =>
        {
            var text = entMan.System<ExamineSystemShared>().GetExamineText(shell, viewer).ToMarkup();
            Assert.That(text, Does.Contain("MOD core"));
            Assert.That(text, Does.Not.Contain("construction-graph-tag-mod-core"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The built-in night vision module shows and clears the wearer's overlay when toggled.</summary>
    [Test]
    public async Task NightVisionModuleShowsOverlay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var modSuit = entMan.System<ModSuitSystem>();
        var inventory = entMan.System<InventorySystem>();
        var minds = entMan.System<SharedMindSystem>();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);
        var map = await pair.CreateTestMap();

        EntityUid wearer = default;
        EntityUid suit = default;
        EntityUid module = default;

        await server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            wearer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, wearer);
            suit = entMan.SpawnEntity("ClothingModsuitSyndicate", map.GridCoords);
            Assert.That(inventory.TryEquip(wearer, suit, "back", force: true), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            module = comp.ModulesContainer.ContainedEntities.Single(m =>
                entMan.GetComponent<MetaDataComponent>(m).EntityPrototype?.ID == "ModsuitModuleBuiltinNightVision");
            modSuit.ToggleAllParts((suit, comp), wearer);
        });
        await server.WaitRunTicks(150);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(modSuit.TryStartActivation((suit, comp), wearer), Is.True);
        });
        await server.WaitRunTicks(360);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<ModSuitComponent>(suit);
            Assert.That(comp.State, Is.EqualTo(ModSuitState.Active));
            Assert.That(modSuit.TryUseModule(module, wearer), Is.True, "The night vision module didn't toggle on.");
        });
        await pair.RunTicksSync(20);

        bool Overlay() => client.ResolveDependency<IOverlayManager>().HasOverlay<NightVisionOverlay>();
        await client.WaitAssertion(() => Assert.That(Overlay(), Is.True, "The night vision module gave no overlay."));

        await server.WaitAssertion(() =>
            Assert.That(modSuit.TryUseModule(module, wearer), Is.True, "The night vision module didn't toggle off."));
        await pair.RunTicksSync(20);

        await client.WaitAssertion(() => Assert.That(Overlay(), Is.False, "The night vision overlay stayed on."));

        await pair.CleanReturnAsync();
    }

    /// <summary>The resin mister's foam leaves one wall per tile instead of stacking two.</summary>
    [Test]
    public async Task ResinMisterFoamLeavesOneWall()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var foam = entMan.SpawnEntity("AluminiumMetalFoam", map.GridCoords);
            entMan.EnsureComponent<TimedDespawnComponent>(foam).Lifetime = 0.1f;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        var walls = entMan.AllEntities<MetaDataComponent>()
            .Count(e => e.Comp.EntityPrototype?.ID == "FoamedAluminiumMetal");
        Assert.That(walls, Is.EqualTo(1), "metal foam should leave exactly one wall");

        await pair.CleanReturnAsync();
    }

    /// <summary>The microreactor core slowly refills its own battery.</summary>
    [Test]
    public async Task MicroreactorCoreRechargesItself()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var batteries = entMan.System<BatterySystem>();

        EntityUid core = default;
        await server.WaitPost(() =>
        {
            core = entMan.SpawnEntity("ModsuitCoreStandardMicroreactor", map.GridCoords);
            batteries.SetCharge(core, 0f);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BatteryComponent>(core).CurrentCharge, Is.GreaterThan(0f),
                "the microreactor core did not recharge");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The MOD health analyzer scans no farther than normal interaction range.</summary>
    [Test]
    public async Task ModsuitHealthAnalyzerUsesInteractionRange()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        var proto = prototypes.Index<EntityPrototype>("ModsuitDeviceHealthAnalyzer");
        Assert.That(proto.TryComp<HealthAnalyzerComponent>(out var analyzer, factory), Is.True);
        Assert.That(analyzer!.MaxScanRange, Is.LessThanOrEqualTo(1.5f));

        await pair.CleanReturnAsync();
    }

    /// <summary>A cards drawn at the same spot hide each other, so MODsuit techs must keep clear of other research.</summary>
    [Test]
    public async Task ModsuitTechnologiesDoNotOverlapOtherResearch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        var occupied = new Dictionary<Vector2i, string>();
        var modsuitPositions = new Dictionary<Vector2i, string>();

        foreach (var tech in prototypes.EnumeratePrototypes<TechnologyPrototype>())
        {
            if (!tech.ID.StartsWith("Modsuit", StringComparison.Ordinal))
            {
                occupied.TryAdd(tech.Position, tech.ID);
                continue;
            }

            Assert.That(occupied.TryGetValue(tech.Position, out var other), Is.False,
                $"{tech.ID} overlaps {other} at {tech.Position}");
            Assert.That(modsuitPositions.TryAdd(tech.Position, tech.ID), Is.True,
                $"{tech.ID} overlaps {modsuitPositions.GetValueOrDefault(tech.Position)} at {tech.Position}");
        }

        await pair.CleanReturnAsync();
    }
}
