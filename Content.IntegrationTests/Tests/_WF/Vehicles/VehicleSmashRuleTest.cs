#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._WF.Vehicles.Crushing;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Crushing;
using Content.Shared._WF.Vehicles.Grid;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Server.Destructible;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Item;
using Content.Shared.Storage.Components;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>The smash rules, checked against every entity prototype: they match what they should and never what they must not.</summary>
[TestFixture]
public sealed class VehicleSmashRuleTest
{
    /// <summary>The most damage a thing may take to be destroyed and still be smashed; anything tougher is as good as indestructible.</summary>
    private const double MaxDestroyDamage = 1000;

    /// <summary>The same for ship hull, which only a tank breaks: shuttle windows and docking airlocks take 4000, a capital ship's 7000.</summary>
    private const double MaxHullDestroyDamage = 10000;

    /// <summary>Components of the machines of a ship, of things that keep player property, and of the grid itself.</summary>
    private static readonly string[] ForbiddenComponents =
    {
        "MapGrid", "ProtectedGrid", "AsteroidRock", "Thruster", "WFLandingThruster", "ShuttleConsole",
        "FTLDrive", "FTLBeacon", "FTLSmashImmune", "StationAnchor", "ShipShield", "ShipShieldEmitter",
        "Smes", "PowerSupplier", "PortableGenerator", "FuelGenerator", "NuclearReactor", "TegGenerator", "GravityGenerator",
        "SafetyDepositBox", "SafetyDepositConsole", "BankATM", "StationBankATM", "ShipyardConsole", "TradeCrate",
        "CargoPallet", "Item",
    };

    private static readonly string[] ForbiddenTags = { "Wall", "HighSecDoor", "SafetyDepositBox", "SafetyDepositBlacklist" };

    /// <summary>
    /// Prototypes whose descendants are armour, safes, walls, or fixtures of an outpost, which are built many times tougher
    /// than a ship's on purpose and are no ship.
    /// </summary>
    private static readonly string[] ForbiddenAncestors =
    {
        "BaseWall", "BaseWallIndestructible", "BaseStructureIndestructible", "GunSafe", "N14ClosetSafe", "BlastDoor",
        "N14WallWoodFrame",
        "ReinforcedWindowOutpost", "ReinforcedWindowDiagonalOutpost", "PlastitaniumWindowOutpost", "PlastitaniumWindowDiagonalOutpost",
        "AirlockExternalGlassShuttleMieyoLockedOutpost", "AirlockExternalShuttleMieyoLockedOutpost", "AirlockGlassShuttleNfsdOutpost",
        "AirlockGlassShuttleOutpost", "AirlockGlassShuttleUSSPOutpostLocked", "AirlockShuttleNfsdOutpost", "AirlockShuttleOutpost",
        "AirlockShuttleUSSPOutpostLocked",
    };

    /// <summary>
    /// Prototypes whose descendants are the hull of a ship: the windows and airlocks ships are built from. Breakable by tanks
    /// only, so a rule that matches one must set a minimum destroy weight class of Medium or more (owner decision, 2026-10-11).
    /// </summary>
    private static readonly string[] HullAncestors =
    {
        "ReinforcedWindow", "ReinforcedPlasmaWindow", "ReinforcedUraniumWindow", "WindowReinforcedDirectional",
        "PlasmaReinforcedWindowDirectional", "UraniumReinforcedWindowDirectional", "ShuttleWindow", "PlastitaniumWindowBase",
        "AirlockExternal", "AirlockShuttle",
    };

    /// <summary>Components of hull parts that need no prototype of the list to be recognised: any docking port.</summary>
    private static readonly string[] HullComponents = { "Docking" };

    /// <summary>The rules for what is stored or sold. The economy's lockers, crates and vendors are no hull and no vehicle breaks them (owner decision, 2026-10-11).</summary>
    private static readonly string[] EconomyRules = { "WFVehicleSmashLockers", "WFVehicleSmashCrates", "WFVehicleSmashVendingMachines" };

    /// <summary>Components of access-locked and secure storage, of trade goods and of stores, uplinks and company vendors.</summary>
    private static readonly string[] EconomyComponents =
    {
        "Lock", "AccessReader", "CompanyAccessReader", "CurrencyInjectionTarget", "Store", "TradeCrate",
    };

    /// <summary>
    /// Prototypes whose descendants are economy fixtures: secure lockers and crates, the vendors that cannot be unanchored
    /// (trade stations, outposts and points of interest, the traders' stock, weapon and faction vendors), the bounty
    /// vendors and the ASF grant kiosk.
    /// </summary>
    private static readonly string[] EconomyAncestors =
    {
        "LockerBaseSecure", "NFLockerBaseSecure", "CrateBaseSecure", "BaseStructureDisableAnchoring",
        "VendingMachineTraderBaikal", "VendingMachineBountyVend", "VendingMachineBountyVendPunk", "WFAsfGrantKiosk",
    };

    private static readonly Regex SafeName = new(@"\b(safes?|vault|deposit)\b", RegexOptions.IgnoreCase);

    /// <summary>Entity prototypes with every rule that matches them, in winning order.</summary>
    private static List<(EntityPrototype Proto, List<VehicleSmashRulePrototype> Rules)> Matches(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var rules = server.System<VehicleSmashRuleSystem>();
        var result = new List<(EntityPrototype, List<VehicleSmashRulePrototype>)>();

        foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
        {
            var hits = rules.MatchingRules(proto).ToList();
            if (hits.Count > 0)
                result.Add((proto, hits));
        }

        return result;
    }

    /// <summary>
    /// A driver's client predicts the drive, so it has to know what the vehicle smashes: whatever the server's rules give
    /// a prototype, the client's give it too. The client has no Destructible component to go by.
    /// </summary>
    [Test]
    public async Task TheClientFindsEveryRuleTheServerDoes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var smashable = new List<string>();
        var missing = new List<string>();

        await server.WaitPost(() =>
        {
            var rules = server.System<VehicleSmashRuleSystem>();
            foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (rules.TryGetSettings(proto, out _))
                    smashable.Add(proto.ID);
            }
        });

        await client.WaitPost(() =>
        {
            var rules = client.System<VehicleSmashRuleSystem>();
            foreach (var id in smashable)
            {
                if (!client.ProtoMan.TryIndex<EntityPrototype>(id, out var proto) || !rules.TryGetSettings(proto, out _))
                    missing.Add(id);
            }
        });

        Assert.That(smashable, Is.Not.Empty, "Test setup: the server's rules match nothing.");
        Assert.That(missing, Is.Empty, $"The client would be stopped by {missing.Count} things the server smashes, such as {string.Join(", ", missing.Take(8))}.");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The universal check: every prototype a rule matches is something that can be broken and may be smashed. It reads
    /// the prototypes, not the rules, so a rule that is written too wide is found here.
    /// </summary>
    [Test]
    public async Task NoRuleMatchesAWallAnIndestructibleThingOrAProtectedOne()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var failures = new List<string>();
        var perRule = new Dictionary<string, int>();

        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan;

            foreach (var (proto, hits) in Matches(server))
            {
                var ancestors = protos.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true).Select(p => p.id).ToHashSet();
                var tags = VehicleSmashRuleSystem.TryGetTags(proto, out var found)
                    ? found.Select(t => t.Id).ToHashSet()
                    : new HashSet<string>();

                void Fail(string why) => failures.Add($"{proto.ID} ({hits[0].ID}): {why}");

                if (hits.Count > 1 && hits[0].Priority == hits[1].Priority)
                    Fail($"{hits[0].ID} and {hits[1].ID} match it at the same priority");

                perRule[hits[0].ID] = perRule.GetValueOrDefault(hits[0].ID) + 1;

                foreach (var component in ForbiddenComponents)
                {
                    if (proto.Components.ContainsKey(component))
                        Fail($"has the {component} component");
                }

                foreach (var tag in ForbiddenTags.Where(tags.Contains))
                {
                    Fail($"is tagged {tag}");
                }

                foreach (var ancestor in ForbiddenAncestors.Where(ancestors.Contains))
                {
                    Fail($"is, or inherits from, {ancestor}");
                }

                if (EconomyRules.Contains(hits[0].ID))
                {
                    foreach (var component in EconomyComponents.Where(proto.Components.ContainsKey))
                    {
                        Fail($"is economy ({component} component) and no vehicle may break it");
                    }

                    foreach (var ancestor in EconomyAncestors.Where(ancestors.Contains))
                    {
                        Fail($"is economy (is, or inherits from, {ancestor}) and no vehicle may break it");
                    }
                }

                var isHull = HullAncestors.Any(ancestors.Contains) || HullComponents.Any(proto.Components.ContainsKey);
                var tankOnly = hits[0].MinDestroyWeightClass is { } minimum && minimum >= VehicleWeightClass.Medium;
                if (isHull && !tankOnly)
                    Fail("is ship hull, which only a tank may break, and its rule lets a lighter vehicle break it");

                if (SafeName.IsMatch(proto.Name))
                    Fail($"is called '{proto.Name}'");

                if (!proto.Components.TryGetValue("Damageable", out var damageableEntry) ||
                    damageableEntry.Component is not DamageableComponent damageable)
                {
                    Fail("cannot take damage");
                    continue;
                }

                if (!SupportsBlunt(protos, damageable.DamageContainerID?.Id))
                    Fail($"its damage container {damageable.DamageContainerID} takes no blunt damage");

                if (!proto.Components.TryGetValue("Destructible", out var destructibleEntry) ||
                    destructibleEntry.Component is not DestructibleComponent destructible)
                {
                    Fail("cannot be destroyed");
                    continue;
                }

                if (!VehicleCrushingSystem.TryGetDestructionDamage(destructible, out var destroyedAt))
                    Fail("has no threshold that destroys it");
                else if (destroyedAt > (tankOnly ? MaxHullDestroyDamage : MaxDestroyDamage))
                    Fail($"takes {destroyedAt} damage to destroy, over {(tankOnly ? MaxHullDestroyDamage : MaxDestroyDamage)}");
                else if (hits[0].DeleteOnHit && destroyedAt > hits[0].DamageOnHit)
                    Fail($"takes {destroyedAt} damage to destroy, more than the {hits[0].DamageOnHit} a hit deals, so it would be deleted outright");

                if (proto.Components.TryGetValue("EntityStorage", out var storage) &&
                    storage.Component is SharedEntityStorageComponent { DeleteContentsOnDestruction: true })
                {
                    Fail("deletes its contents when destroyed");
                }
            }
        });

        foreach (var (rule, count) in perRule.OrderBy(p => p.Key))
        {
            TestContext.Out.WriteLine($"{rule}: {count} prototypes");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));

        await pair.CleanReturnAsync();
    }

    private static bool SupportsBlunt(IPrototypeManager protos, string? containerId)
    {
        if (containerId == null)
            return true;

        var container = protos.Index<DamageContainerPrototype>(containerId);
        if (container.SupportedTypes.Contains("Blunt"))
            return true;

        return container.SupportedGroups.Any(group => protos.Index<DamageGroupPrototype>(group).DamageTypes.Contains("Blunt"));
    }

    /// <summary>Every rule is well formed and matches something, so a typo in a parent or a component does not leave it dead.</summary>
    [Test]
    public async Task EveryRuleIsWellFormedAndMatchesSomething()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var failures = new List<string>();

        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan;
            var factory = server.ResolveDependency<IComponentFactory>();
            var used = Matches(server).SelectMany(m => m.Rules).Select(r => r.ID).ToHashSet();

            foreach (var rule in protos.EnumeratePrototypes<VehicleSmashRulePrototype>())
            {
                if (rule.Parents.Count == 0 && rule.Whitelist == null)
                    failures.Add($"{rule.ID}: has no parents and no whitelist, so it matches nothing");

                if (!used.Contains(rule.ID))
                    failures.Add($"{rule.ID}: matches no entity prototype");

                foreach (var (kind, list) in new[] { ("whitelist", rule.Whitelist), ("blacklist", rule.Blacklist) })
                {
                    if (list == null)
                        continue;

                    if (list.MindRoles != null || list.Sizes != null)
                        failures.Add($"{rule.ID}: its {kind} sets mind roles or sizes, which a prototype cannot be checked for");

                    foreach (var name in list.Components ?? System.Array.Empty<string>())
                    {
                        if (!factory.TryGetRegistration(name, out _))
                            failures.Add($"{rule.ID}: its {kind} names the unknown component {name}");
                    }

                    foreach (var tag in list.Tags ?? new List<Robust.Shared.Prototypes.ProtoId<TagPrototype>>())
                    {
                        if (!protos.HasIndex<TagPrototype>(tag.Id))
                            failures.Add($"{rule.ID}: its {kind} names the unknown tag {tag}");
                    }
                }

                foreach (var parent in rule.Parents.Concat(rule.ExcludeParents))
                {
                    if (!protos.HasMapping<EntityPrototype>(parent.Id))
                        failures.Add($"{rule.ID}: names the unknown prototype {parent}");
                }

                if (rule.DamageOnHit <= 0)
                    failures.Add($"{rule.ID}: deals no damage");
            }
        });

        Assert.That(failures, Is.Empty, string.Join("\n", failures));

        await pair.CleanReturnAsync();
    }

    [TestCase("Table", "WFVehicleSmashTables")]
    [TestCase("TableWood", "WFVehicleSmashTables")]
    [TestCase("Rack", "WFVehicleSmashTables")]
    [TestCase("ClosetEmergency", "WFVehicleSmashLockers")]
    [TestCase("WardrobeBlack", "WFVehicleSmashLockers")]
    [TestCase("CrateGeneric", "WFVehicleSmashCrates")]
    [TestCase("CrateMedical", "WFVehicleSmashCrates")]
    [TestCase("MetalBarrelGrey", "WFVehicleSmashBarrels")]
    [TestCase("Bed", "WFVehicleSmashBeds")]
    [TestCase("BenchSofaMiddle", "WFVehicleSmashSofas")]
    [TestCase("VendingMachineCola", "WFVehicleSmashVendingMachines")]
    [TestCase("FenceMetalStraight", "WFVehicleSmashFences")]
    [TestCase("FenceWoodHighStraight", "WFVehicleSmashFences")]
    [TestCase("Barricade", "WFVehicleSmashBarricades")]
    [TestCase("Window", "WFVehicleSmashWindows")]
    [TestCase("PlasmaWindow", "WFVehicleSmashWindows")]
    [TestCase("WindowDirectional", "WFVehicleSmashWindows")]
    [TestCase("ReinforcedWindow", "WFVehicleSmashHullWindows")]
    [TestCase("ReinforcedWindowDiagonal", "WFVehicleSmashHullWindows")]
    [TestCase("ReinforcedPlasmaWindow", "WFVehicleSmashHullWindows")]
    [TestCase("ReinforcedUraniumWindow", "WFVehicleSmashHullWindows")]
    [TestCase("WindowReinforcedDirectional", "WFVehicleSmashHullWindows")]
    [TestCase("ShuttleWindow", "WFVehicleSmashHullWindows")]
    [TestCase("ShuttleWindowDiagonal", "WFVehicleSmashHullWindows")]
    [TestCase("PlastitaniumWindow", "WFVehicleSmashHullWindows")]
    [TestCase("PlastitaniumWindowDiagonal", "WFVehicleSmashHullWindows")]
    [TestCase("AirlockExternal", "WFVehicleSmashHullDoors")]
    [TestCase("AirlockExternalGlass", "WFVehicleSmashHullDoors")]
    [TestCase("AirlockExternalShuttleLocked", "WFVehicleSmashHullDoors")]
    [TestCase("AirlockShuttle", "WFVehicleSmashHullDoors")]
    [TestCase("AirlockGlassShuttle", "WFVehicleSmashHullDoors")]
    [TestCase("Grille", "WFVehicleSmashWindowFrames")]
    [TestCase("Windoor", "WFVehicleSmashWindoors")]
    [TestCase("WindoorSecure", "WFVehicleSmashWindoors")]
    [TestCase("Airlock", "WFVehicleSmashDoors")]
    [TestCase("AirlockEngineeringLocked", "WFVehicleSmashDoors")]
    [TestCase("Firelock", "WFVehicleSmashDoors")]
    [TestCase("ShuttersNormal", "WFVehicleSmashShutters")]
    [TestCase("ShuttersRadiation", "WFVehicleSmashShutters")]
    [TestCase("FloraTree", "WFVehicleSmashTrees")]
    [TestCase("FloraRockSolid", "WFVehicleSmashBoulders")]
    [TestCase("hydroponicsTray", "WFVehicleSmashHydroponicsTrays")]
    [TestCase("DisposalUnit", "WFVehicleSmashDisposalUnits")]
    public async Task APrototypeGetsItsRule(string prototype, string rule)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var rules = server.System<VehicleSmashRuleSystem>();
            var hits = rules.MatchingRules(server.ProtoMan.Index<EntityPrototype>(prototype)).Select(r => r.ID).ToList();

            Assert.That(hits, Is.EqualTo(new[] { rule }), $"{prototype} should get exactly {rule}.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The things the owner named as off limits, and a few neighbours of what is smashed that look alike.</summary>
    [TestCase("WallSolid")]
    [TestCase("WallReinforced")]
    [TestCase("WallSolidDiagonal")]
    [TestCase("Girder")]
    [TestCase("ReinforcedGirder")]
    [TestCase("Gyroscope")]
    [TestCase("ThrusterLargeSecurity")]
    [TestCase("ComputerShuttle")]
    [TestCase("GeneratorBasic")]
    [TestCase("PortableGeneratorPacman")]
    [TestCase("ShieldGenerator")]
    [TestCase("GunSafe")]
    [TestCase("GunSafeSr")]
    [TestCase("SafetyDepositBoxLarge")]
    [TestCase("SafetyDepositConsole")]
    [TestCase("PlastitaniumWindowIndestructible")]
    [TestCase("ReinforcedWindowOutpost")]
    [TestCase("ReinforcedWindowDiagonalOutpost")]
    [TestCase("PlastitaniumWindowOutpost")]
    [TestCase("PlastitaniumWindowDiagonalOutpost")]
    [TestCase("AirlockShuttleOutpost")]
    [TestCase("AirlockGlassShuttleOutpost")]
    [TestCase("AirlockShuttleNfsdOutpostLocked")]
    [TestCase("AirlockExternalShuttleMieyoLockedOutpost")]
    [TestCase("AirlockHeavyGlass")]
    [TestCase("HighSecDoor")]
    [TestCase("BlastDoor")]
    [TestCase("OperatingTable")]
    [TestCase("N14WallWoodFrame")]
    [TestCase("Floodlight")]
    // Economy, which no vehicle breaks: secure and trade crates, access-locked lockers (the evidence lockers among them),
    // and the vendors of trade stations, traders, bounty hunters, the ASF, uplinks and factions.
    [TestCase("CrateTradeSecureNormal")]
    [TestCase("CrateSecgear")]
    [TestCase("CrateArmoryPistols")]
    [TestCase("CrateNfsdSecure1")]
    [TestCase("CrateWeaponSecure")]
    [TestCase("WFCrateArmsVault")]
    [TestCase("LockerSteel")]
    [TestCase("LockerEvidence")]
    [TestCase("LockerNfsdEvidence")]
    [TestCase("LockerCaptain")]
    [TestCase("LockerMaterials")]
    [TestCase("VendingMachineTraderBaikal")]
    [TestCase("VendingMachineTraderDepot")]
    [TestCase("VendingMachineTraderTradeMall")]
    [TestCase("WFTraderStockGeneral")]
    [TestCase("WFTraderStockShipwright")]
    [TestCase("VendingMachineBountyVend")]
    [TestCase("VendingMachineBountyVendPOI")]
    [TestCase("VendingMachineBountyVendPunk")]
    [TestCase("WFAsfGrantKiosk")]
    [TestCase("VendingMachineMMCUplink")]
    [TestCase("VendingMachineSyndieContraband")]
    [TestCase("VendingMachineSec")]
    [TestCase("VendingMachineAmmo")]
    [TestCase("VendingMachineAmmoPOI")]
    public async Task APrototypeMatchesNoRule(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var protos = server.ProtoMan;
            if (!protos.TryIndex<EntityPrototype>(prototype, out var proto))
            {
                Assert.Fail($"{prototype} does not exist; the list of protected prototypes needs updating.");
                return;
            }

            var rules = server.System<VehicleSmashRuleSystem>();
            var hits = rules.MatchingRules(proto).Select(r => r.ID).ToList();

            Assert.That(hits, Is.Empty, $"{prototype} should not be smashed.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A component on the prototype wins over the rule, and a prototype the rules do not know falls back to nothing.</summary>
    [Test]
    public async Task TheComponentWinsOverTheRule()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var table = EntityUid.Invalid;
        var girder = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            table = entMan.SpawnEntity("Table", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            girder = entMan.SpawnEntity("Girder", new EntityCoordinates(map.Grid.Owner, 2.5f, 0.5f));
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var mover = server.System<GridVehicleMoverSystem>();

            Assert.That(mover.TryGetSmashable(table, out var byRule), Is.True, "A table is smashed by rule.");
            Assert.That(byRule.DeleteOnHit, Is.True);
            Assert.That(byRule.MinDestroyWeightClass, Is.Null);
            Assert.That(mover.TryGetSmashable(girder, out _), Is.False, "A girder is not smashed by anything.");

            var smashable = entMan.AddComponent<VehicleSmashableComponent>(table);
            smashable.DeleteOnHit = false;
            smashable.MinDestroyWeightClass = VehicleWeightClass.Heavy;

            Assert.That(mover.TryGetSmashable(table, out var byComponent), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(byComponent.DeleteOnHit, Is.False, "The component should win over the rule.");
                Assert.That(byComponent.MinDestroyWeightClass, Is.EqualTo(VehicleWeightClass.Heavy));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The driver's messages are in Fluent and format with the names of what they are about.</summary>
    [Test]
    public async Task TheDriverMessagesAreFluent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var vehicle = EntityUid.Invalid;
        var door = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            vehicle = entMan.SpawnEntity("WFVehicleTestChassis", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            door = entMan.SpawnEntity("Airlock", new EntityCoordinates(map.Grid.Owner, 3.5f, 0.5f));
        });

        await server.WaitAssertion(() =>
        {
            foreach (var key in new[] { "wf-vehicle-crush-too-light", "wf-vehicle-crush-door-powered" })
            {
                var text = Loc.GetString(key, ("vehicle", vehicle), ("target", door));
                Assert.That(text, Is.Not.EqualTo(key), $"{key} is not in the locale.");
                Assert.That(text, Does.Contain("airlock").IgnoreCase.Or.Contain("door").IgnoreCase);
            }
        });

        await pair.CleanReturnAsync();
    }
}
