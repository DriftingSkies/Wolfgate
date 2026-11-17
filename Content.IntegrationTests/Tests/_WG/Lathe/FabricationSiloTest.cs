using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.Lathe;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Destructible;
using Content.Server.Lathe;
using Content.Server.Power.Components;
using Content.Server.Storage.Components;
using Content.Shared._WG.Lathe;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Research.Prototypes;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WG.Lathe;

/// <summary>
/// Fabrication silos: pouring chemicals and inserting parts, withdrawing and discarding chemicals, linking lathes, and drawing stock.
/// </summary>
[TestFixture]
public sealed class FabricationSiloTest
{
    // A lathe that links to silos and one that does not, each with a recipe nothing else uses.
    [TestPrototypes]
    private const string Prototypes = @"
- type: latheRecipe
  id: WGTestSiloRecipe
  result: CapacitorEconomy1
  completetime: 1
  entities:
    Wirecutter: 1
  reagents:
    Lithium: 5

- type: latheRecipePack
  id: WGTestSiloPack
  recipes:
  - WGTestSiloRecipe

- type: entity
  id: WGTestSiloLathe
  components:
  - type: Lathe
    staticPacks:
    - WGTestSiloPack
  - type: FabricationSiloClient

- type: latheRecipe
  id: WGTestUnlinkedRecipe
  result: CapacitorEconomy1
  completetime: 1
  entities:
    Welder: 1
  reagents:
    Sodium: 5

- type: latheRecipePack
  id: WGTestUnlinkedPack
  recipes:
  - WGTestUnlinkedRecipe

- type: entity
  id: WGTestUnlinkedLathe
  components:
  - type: Lathe
    staticPacks:
    - WGTestUnlinkedPack
";

    private const string Superconductant = "Superconductant";
    private const string Silicon = "Silicon";
    private const string Carbon = "Carbon";
    private const string Water = "Water";
    private const string TestReagent = "Lithium";
    private const string UnlinkedReagent = "Sodium";
    private static readonly EntProtoId TestPart = "Wirecutter";
    private static readonly EntProtoId UnlinkedPart = "Welder";
    private static readonly ProtoId<LatheRecipePrototype> AltCapacitorRecipe = "CapacitorEconomy2Alt";
    private static readonly EntProtoId MaterialSiloBoard = "MaterialSiloMachineCircuitboard";

    [TestCase("JerryCan", Superconductant, 250, true)]
    [TestCase("JerryCanWeldingFuel", "WeldingFuel", 0, false)]
    [TestCase("JerryCanNaniteFuel", "NaniteFuel", 0, false)]
    [TestCase("Beaker", Silicon, 30, true)]
    [TestCase("Beaker", Water, 30, false)]
    public async Task PourIntoChemicalSilo(string prototype, string reagent, int added, bool accepted)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var container = entities.SpawnEntity(prototype, map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var solutions = server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetDrainableSolution(container, out var solution, out var contents), Is.True);
            if (added > 0)
                Assert.That(solutions.TryAddReagent(solution!.Value, reagent, added), Is.True);
            var expected = contents!.Volume;
            Assert.That(expected, Is.GreaterThan(FixedPoint2.Zero));

            Assert.That(server.System<FabricationSiloSystem>().IsAcceptedReagent(reagent), Is.EqualTo(accepted));

            entities.GetComponent<SolutionTransferComponent>(container).TransferAmount = expected;
            Assert.That(Pour(server, user, container, silo, map.GridCoords), Is.True);

            var stock = SiloSolution(server, silo);
            Assert.That(stock.GetTotalPrototypeQuantity(reagent), Is.EqualTo(accepted ? expected : FixedPoint2.Zero));
            Assert.That(contents.Volume, Is.EqualTo(accepted ? FixedPoint2.Zero : expected),
                "A refused reagent stays in its container.");

            entities.DeleteEntity(container);
            entities.DeleteEntity(silo);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PouringStopsAtTankVolume()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var beaker = entities.SpawnEntity("Beaker", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var solutions = server.System<SharedSolutionContainerSystem>();
            var silos = server.System<FabricationSiloSystem>();
            Assert.That(silos.TryGetSolution((silo, entities.GetComponent<FabricationSiloComponent>(silo)),
                out var siloSoln,
                out var stock), Is.True);
            Assert.That(stock!.MaxVolume, Is.EqualTo(FixedPoint2.New(1500)));
            Assert.That(solutions.TryAddReagent(siloSoln!.Value, "Water", 1490), Is.True);

            Assert.That(solutions.TryGetDrainableSolution(beaker, out var solution, out var contents), Is.True);
            Assert.That(solutions.TryAddReagent(solution!.Value, Superconductant, 30), Is.True);
            entities.GetComponent<SolutionTransferComponent>(beaker).TransferAmount = 30;

            Pour(server, user, beaker, silo, map.GridCoords);
            Assert.That(stock.Volume, Is.EqualTo(FixedPoint2.New(1500)), "A pour only fills the silo to its cap.");
            Assert.That(contents!.Volume, Is.EqualTo(FixedPoint2.New(20)), "What did not fit stays in the beaker.");

            Pour(server, user, beaker, silo, map.GridCoords);
            Assert.That(stock.Volume, Is.EqualTo(FixedPoint2.New(1500)));
            Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(20)), "A full silo refuses the pour.");

            entities.DeleteEntity(beaker);
            entities.DeleteEntity(silo);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StoredChemicalsKeepDataAndDoNotReact()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var first = entities.SpawnEntity("Beaker", map.GridCoords);
            var second = entities.SpawnEntity("Beaker", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var solutions = server.System<SharedSolutionContainerSystem>();
            var data = new List<ReagentData> { new DnaData { DNA = "WGTEST" } };

            Assert.That(solutions.TryGetDrainableSolution(first, out var firstSoln, out _), Is.True);
            solutions.AddSolution(firstSoln!.Value, new Solution(Superconductant, 10, data));
            Assert.That(solutions.TryAddReagent(firstSoln.Value, Silicon, 5), Is.True);
            Assert.That(solutions.TryGetDrainableSolution(second, out var secondSoln, out _), Is.True);
            Assert.That(solutions.TryAddReagent(secondSoln!.Value, Carbon, 5), Is.True);
            entities.GetComponent<SolutionTransferComponent>(first).TransferAmount = 20;
            entities.GetComponent<SolutionTransferComponent>(second).TransferAmount = 5;

            Pour(server, user, first, silo, map.GridCoords);
            Pour(server, user, second, silo, map.GridCoords);

            var stock = SiloSolution(server, silo);
            Assert.That(stock.GetReagentQuantity(new ReagentId(Superconductant, data)), Is.EqualTo(FixedPoint2.New(10)),
                "Reagent data survives the pour.");
            Assert.That(stock.GetTotalPrototypeQuantity("Kelotane"), Is.EqualTo(FixedPoint2.Zero),
                "Reagents in the silo must not react.");
            Assert.That(stock.GetTotalPrototypeQuantity(Silicon), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(stock.GetTotalPrototypeQuantity(Carbon), Is.EqualTo(FixedPoint2.New(5)));

            entities.DeleteEntity(first);
            entities.DeleteEntity(second);
            entities.DeleteEntity(silo);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("WGPartsSiloMachineCircuitboard", "WGMachinePartsSilo")]
    [TestCase("WGChemicalSiloMachineCircuitboard", "WGMachineChemicalSilo")]
    public async Task SiloBoardsNeedMaterialSiloParts(string board, string machine)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var factory = server.ResolveDependency<IComponentFactory>();
            Assert.That(server.ProtoMan.Index(MaterialSiloBoard).TryComp<MachineBoardComponent>(out var expected, factory),
                Is.True);
            Assert.That(server.ProtoMan.Index<EntityPrototype>(board).TryComp<MachineBoardComponent>(out var actual, factory),
                Is.True);
            Assert.That(actual!.Requirements, Is.EquivalentTo(expected!.Requirements));
            Assert.That(actual.StackRequirements, Is.EquivalentTo(expected.StackRequirements));
            Assert.That(actual.TagRequirements.Keys, Is.EquivalentTo(expected.TagRequirements.Keys));
            foreach (var (tag, info) in expected.TagRequirements)
            {
                Assert.That(actual.TagRequirements[tag].Amount, Is.EqualTo(info.Amount));
                Assert.That(actual.TagRequirements[tag].DefaultPrototype, Is.EqualTo(info.DefaultPrototype));
            }

            // A spawned machine, as from a flatpack, gets the default parts for its board.
            var uid = entities.SpawnEntity(machine, map.GridCoords);
            var parts = server.System<ContainerSystem>().GetContainer(uid, MachineFrameComponent.PartContainerName);
            foreach (var (_, info) in expected.TagRequirements)
            {
                Assert.That(parts.ContainedEntities.Count(part =>
                        entities.GetComponent<MetaDataComponent>(part).EntityPrototype?.ID == info.DefaultPrototype.Id),
                    Is.EqualTo(info.Amount));
            }

            entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RejectNonComponentsWithoutRemovingThem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachinePartsSilo", map.GridCoords);
            var item = entities.SpawnEntity(null, map.GridCoords);
            var component = entities.SpawnEntity("ElectromagnetEconomy1", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var storage = entities.GetComponent<FabricationSiloComponent>(silo).Parts;
            Assert.That(storage, Is.Not.Null);

            entities.EventBus.RaiseLocalEvent(silo, new InteractUsingEvent(user, item, silo, map.GridCoords));
            Assert.That(storage!.Contains(item), Is.False);
            Assert.That(entities.EntityExists(item), Is.True);

            entities.EventBus.RaiseLocalEvent(silo, new InteractUsingEvent(user, component, silo, map.GridCoords));
            Assert.That(storage.Contains(component), Is.True);

            entities.DeleteEntity(silo);
            entities.DeleteEntity(item);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ToolIngredientsAreNotStoredByUse()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachinePartsSilo", map.GridCoords);
            var lathe = entities.SpawnEntity("PrecisionAssemblerEconomyCompact", map.GridCoords);
            var tool = entities.SpawnEntity("Multitool", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            Assert.That(server.System<FabricationSiloSystem>().IsAcceptedPart((EntProtoId) "Multitool"), Is.True);

            entities.EventBus.RaiseLocalEvent(silo, new InteractUsingEvent(user, tool, silo, map.GridCoords));
            var stored = entities.GetComponent<FabricationSiloComponent>(silo).Parts;
            Assert.That(stored!.Contains(tool), Is.False);

            entities.EventBus.RaiseLocalEvent(lathe, new InteractUsingEvent(user, tool, lathe, map.GridCoords));
            Assert.That(entities.GetComponent<EntityStorageComponent>(lathe).Contents.Contains(tool), Is.False);

            entities.DeleteEntity(silo);
            entities.DeleteEntity(lathe);
            entities.DeleteEntity(tool);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IndependentLinksAndImmediateConsumption()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var machine = entities.SpawnEntity("PrecisionAssemblerEconomyCompact", map.GridCoords);
            var parts = entities.SpawnEntity("WGMachinePartsSilo", map.GridCoords);
            var chemicals = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var silos = server.System<FabricationSiloSystem>();
            var containers = server.System<ContainerSystem>();
            var partsStore = entities.GetComponent<FabricationSiloComponent>(parts);
            var links = entities.GetComponent<FabricationSiloClientComponent>(machine);

            Power(server, parts);
            Power(server, chemicals);
            Toggle(server, parts, machine);
            Toggle(server, chemicals, machine);

            Assert.That(links.PartsSilo, Is.EqualTo(parts));
            Assert.That(links.ChemicalSilo, Is.EqualTo(chemicals));
            Assert.That(entities.TryGetComponent<OreSiloClientComponent>(machine, out var oreLink) && oreLink.Silo != null,
                Is.False,
                "Fabrication silo links must not touch the material silo link.");
            Assert.That(silos.GetLinkedSilo(machine, FabricationSiloKind.Parts)?.Owner, Is.EqualTo(parts));
            Assert.That(silos.GetLinkedSilo(machine, FabricationSiloKind.Chemicals)?.Owner, Is.EqualTo(chemicals));
            Assert.That(partsStore.Clients, Does.Contain(machine));

            for (var i = 0; i < 3; i++)
            {
                containers.Insert(entities.SpawnEntity("ElectromagnetEconomy1", map.GridCoords), partsStore.Parts!);
            }

            AddToSilo(server, chemicals, new Solution(Superconductant, 30));
            Assert.That(silos.GetPartAmount(machine, "ElectromagnetEconomy1"), Is.EqualTo(3));
            Assert.That(silos.ConsumeParts(machine, "ElectromagnetEconomy1", 2), Is.EqualTo(2));
            Assert.That(silos.GetPartAmount(machine, "ElectromagnetEconomy1"), Is.EqualTo(1),
                "Consumed parts must disappear immediately, before deferred entity deletion.");
            Assert.That(silos.ConsumeReagent(machine, Superconductant, 31), Is.False, "A short silo gives nothing.");
            Assert.That(silos.ConsumeReagent(machine, Superconductant, 30), Is.True);
            Assert.That(SiloSolution(server, chemicals).Volume, Is.EqualTo(FixedPoint2.Zero));

            Toggle(server, parts, machine);
            Assert.That(links.PartsSilo, Is.Null);
            Assert.That(partsStore.Clients, Does.Not.Contain(machine));
            Assert.That(links.ChemicalSilo, Is.EqualTo(chemicals));

            entities.DeleteEntity(chemicals);
            Assert.That(links.ChemicalSilo, Is.Null, "Deleting a silo clears its links.");

            entities.DeleteEntity(machine);
            entities.DeleteEntity(parts);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LatheDrawsFromLinkedSilos()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var machine = entities.SpawnEntity("PrecisionAssemblerEconomyCompact", map.GridCoords);
            var parts = entities.SpawnEntity("WGMachinePartsSilo", map.GridCoords);
            var chemicals = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var lathes = server.System<LatheSystem>();
            var lathe = entities.GetComponent<LatheComponent>(machine);
            var data = new List<ReagentData> { new DnaData { DNA = "WGTEST" } };

            Power(server, machine);
            Power(server, parts);
            Power(server, chemicals);
            Toggle(server, parts, machine);
            Toggle(server, chemicals, machine);
            server.System<ContainerSystem>().Insert(entities.SpawnEntity("CapacitorEconomy1", map.GridCoords),
                entities.GetComponent<FabricationSiloComponent>(parts).Parts!);
            AddToSilo(server, chemicals, new Solution(Superconductant, 40, data));

            var recipe = server.ProtoMan.Index(AltCapacitorRecipe);
            Assert.That(lathes.GetAvailableReagent(machine, lathe, Superconductant), Is.EqualTo(FixedPoint2.New(40)));
            Assert.That(lathes.CanProduce(machine, recipe, 1, lathe), Is.True);
            Assert.That(lathes.TryAddToQueue(machine, recipe, 1, lathe), Is.True);
            Assert.That(lathes.TryStartProducing(machine, lathe), Is.True);

            var stock = SiloSolution(server, chemicals);
            Assert.That(stock.GetReagentQuantity(new ReagentId(Superconductant, data)), Is.EqualTo(FixedPoint2.New(10)),
                "The lathe splits exactly the recipe amount and the rest keeps its data.");
            Assert.That(entities.GetComponent<FabricationSiloComponent>(parts).Parts!.ContainedEntities, Is.Empty);

            entities.DeleteEntity(machine);
            entities.DeleteEntity(parts);
            entities.DeleteEntity(chemicals);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientSeesLinksAndStock()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid machine = default;
        EntityUid parts = default;
        EntityUid chemicals = default;

        await server.WaitAssertion(() =>
        {
            machine = entities.SpawnEntity("PrecisionAssemblerEconomyCompact", map.GridCoords);
            parts = entities.SpawnEntity("WGMachinePartsSilo", map.GridCoords);
            chemicals = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            Power(server, parts);
            Power(server, chemicals);
            Toggle(server, parts, machine);
            Toggle(server, chemicals, machine);
            var store = entities.GetComponent<FabricationSiloComponent>(parts).Parts!;
            for (var i = 0; i < 2; i++)
            {
                server.System<ContainerSystem>().Insert(entities.SpawnEntity("ElectromagnetEconomy1", map.GridCoords), store);
            }

            AddToSilo(server, chemicals, new Solution(Superconductant, 25));
        });

        await pair.RunTicksSync(10);

        await pair.Client.WaitAssertion(() =>
        {
            var clientMachine = pair.ToClientUid(machine);
            var silos = pair.Client.System<SharedFabricationSiloSystem>();
            Assert.That(silos.GetLinkedSilo(clientMachine, FabricationSiloKind.Parts)?.Owner,
                Is.EqualTo(pair.ToClientUid(parts)));
            Assert.That(silos.GetLinkedSilo(clientMachine, FabricationSiloKind.Chemicals)?.Owner,
                Is.EqualTo(pair.ToClientUid(chemicals)));
            Assert.That(silos.GetPartAmount(clientMachine, "ElectromagnetEconomy1"), Is.EqualTo(2));
            Assert.That(silos.GetReagentAmount(clientMachine, Superconductant), Is.EqualTo(FixedPoint2.New(25)));
        });

        await server.WaitPost(() =>
        {
            entities.DeleteEntity(machine);
            entities.DeleteEntity(parts);
            entities.DeleteEntity(chemicals);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WithdrawMovesOnlyTheChosenReagent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var beaker = entities.SpawnEntity("Beaker", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var farUser = entities.SpawnEntity(null, map.GridCoords.Offset(new Vector2(10, 0)));
            var silos = server.System<FabricationSiloSystem>();
            var itemSlots = server.System<ItemSlotsSystem>();
            var ent = (silo, entities.GetComponent<FabricationSiloComponent>(silo));
            var data = new List<ReagentData> { new DnaData { DNA = "WGTEST" } };

            AddToSilo(server, silo, new Solution(Superconductant, 40, data));
            AddToSilo(server, silo, new Solution("Water", 100));
            AddToSilo(server, silo, new Solution("Potassium", 20));
            var stock = SiloSolution(server, silo);

            Assert.That(silos.WithdrawReagent(ent, user, "Water", 10), Is.EqualTo(FixedPoint2.Zero),
                "Nothing is withdrawn without a container in the slot.");
            Assert.That(itemSlots.TryInsert(silo, FabricationSiloComponent.ContainerSlotId, beaker, null), Is.True);
            Assert.That(silos.TryGetSlottedSolution(ent, out _, out _, out var contents), Is.True);

            Assert.That(silos.WithdrawReagent(ent, user, "Water", FixedPoint2.New(-5)), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(silos.WithdrawReagent(ent, user, "Water", FixedPoint2.Zero), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(silos.WithdrawReagent(ent, user, "WGNotAReagent", 10), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(silos.WithdrawReagent(ent, user, "Nitrogen", 10), Is.EqualTo(FixedPoint2.Zero),
                "A reagent the silo does not hold gives nothing.");
            Assert.That(silos.WithdrawReagent(ent, farUser, "Water", 10), Is.EqualTo(FixedPoint2.Zero),
                "A user out of range cannot withdraw.");
            Assert.That(contents!.Volume, Is.EqualTo(FixedPoint2.Zero));

            // Through the window's message, as a client sends it.
            entities.EventBus.RaiseLocalEvent(silo,
                new WithdrawFabricationSiloReagentMessage(Superconductant, 10) { Actor = user });
            Assert.That(contents.GetReagentQuantity(new ReagentId(Superconductant, data)), Is.EqualTo(FixedPoint2.New(10)),
                "The withdrawn reagent keeps its data.");
            Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(10)), "Only the chosen reagent moves.");
            Assert.That(stock.GetTotalPrototypeQuantity(Superconductant), Is.EqualTo(FixedPoint2.New(30)));

            var free = contents.AvailableVolume;
            Assert.That(free, Is.LessThan(FixedPoint2.New(100)), "The test needs more water than the beaker holds.");
            Assert.That(silos.WithdrawReagent(ent, user, "Water", null), Is.EqualTo(free),
                "Withdrawing all stops at the container's free space.");
            Assert.That(contents.GetTotalPrototypeQuantity("Water"), Is.EqualTo(free));
            Assert.That(silos.WithdrawReagent(ent, user, "Water", 5), Is.EqualTo(FixedPoint2.Zero),
                "A full container takes nothing.");

            Assert.That(stock.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(100) - free));
            Assert.That(stock.GetTotalPrototypeQuantity("Potassium"), Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(contents.GetTotalPrototypeQuantity("Potassium"), Is.EqualTo(FixedPoint2.Zero));

            entities.DeleteEntity(silo);
            entities.DeleteEntity(user);
            entities.DeleteEntity(farUser);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DiscardRemovesOnlyTheChosenReagent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var farUser = entities.SpawnEntity(null, map.GridCoords.Offset(new Vector2(10, 0)));
            var silos = server.System<FabricationSiloSystem>();
            var ent = (silo, entities.GetComponent<FabricationSiloComponent>(silo));
            var data = new List<ReagentData> { new DnaData { DNA = "WGTEST" } };

            AddToSilo(server, silo, new Solution(Superconductant, 10, data));
            AddToSilo(server, silo, new Solution(Superconductant, 5));
            AddToSilo(server, silo, new Solution("Water", 50));
            var stock = SiloSolution(server, silo);

            Assert.That(silos.DiscardReagent(ent, farUser, "Water"), Is.EqualTo(FixedPoint2.Zero),
                "A user out of range cannot discard.");
            Assert.That(silos.DiscardReagent(ent, user, "Nitrogen"), Is.EqualTo(FixedPoint2.Zero));

            entities.EventBus.RaiseLocalEvent(silo, new DiscardFabricationSiloReagentMessage(Superconductant) { Actor = user });
            Assert.That(stock.GetTotalPrototypeQuantity(Superconductant), Is.EqualTo(FixedPoint2.Zero),
                "Every data variant of the reagent is discarded.");
            Assert.That(stock.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(50)));
            Assert.That(stock.Volume, Is.EqualTo(FixedPoint2.New(50)));

            entities.DeleteEntity(silo);
            entities.DeleteEntity(user);
            entities.DeleteEntity(farUser);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NothingDrainsTheChemicalSilo()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var beaker = entities.SpawnEntity("Beaker", map.GridCoords);
            var mop = entities.SpawnEntity("MopItem", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var solutions = server.System<SharedSolutionContainerSystem>();
            AddToSilo(server, silo, new Solution("Water", 50));
            AddToSilo(server, silo, new Solution("Potassium", 20));
            var stock = SiloSolution(server, silo);

            Assert.That(entities.HasComponent<DrainableSolutionComponent>(silo), Is.False);
            Assert.That(entities.HasComponent<DrawableSolutionComponent>(silo), Is.False, "Syringes draw from Drawable.");
            Assert.That(entities.HasComponent<DumpableSolutionComponent>(silo), Is.False);
            Assert.That(entities.HasComponent<SpillableComponent>(silo), Is.False);
            Assert.That(entities.GetComponent<RefillableSolutionComponent>(silo).PreventTransferOut, Is.True,
                "The silo cannot be dragged onto a drain.");
            Assert.That(solutions.TryGetDrainableSolution(silo, out _, out _), Is.False);
            Assert.That(solutions.TryGetDrawableSolution(silo, out _, out _), Is.False);
            Assert.That(solutions.TryGetDumpableSolution(silo, out _, out _), Is.False);

            Assert.That(solutions.TryGetDrainableSolution(beaker, out _, out var beakerContents), Is.True);
            Pour(server, user, beaker, silo, map.GridCoords);
            Assert.That(beakerContents!.Volume, Is.EqualTo(FixedPoint2.Zero), "An empty beaker draws nothing.");
            Assert.That(server.System<ItemSlotsSystem>().GetItemOrNull(silo, FabricationSiloComponent.ContainerSlotId),
                Is.Null,
                "Clicking with a beaker pours instead of inserting it.");

            Pour(server, user, mop, silo, map.GridCoords);
            Assert.That(solutions.TryGetSolution(mop, AbsorbentComponent.SolutionName, out _, out var mopContents), Is.True);
            Assert.That(mopContents!.Volume, Is.EqualTo(FixedPoint2.Zero), "A mop pulls no water from the silo.");

            Assert.That(stock.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(50)));
            Assert.That(stock.GetTotalPrototypeQuantity("Potassium"), Is.EqualTo(FixedPoint2.New(20)));

            entities.DeleteEntity(silo);
            entities.DeleteEntity(beaker);
            entities.DeleteEntity(mop);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AbsorbentUserCannotMopTheChemicalSilo(bool stocked)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var bot = entities.SpawnEntity("MobCleanBot", map.GridCoords);
            var solutions = server.System<SharedSolutionContainerSystem>();
            if (stocked)
            {
                AddToSilo(server, silo, new Solution(Water, 50));
                AddToSilo(server, silo, new Solution(Silicon, 20));
            }

            Assert.That(solutions.TryGetSolution(bot, AbsorbentComponent.SolutionName, out var absorbed, out var botContents),
                Is.True);
            Assert.That(solutions.TryAddSolution(absorbed!.Value, new Solution(UnlinkedReagent, 10)), Is.True);
            var stock = SiloSolution(server, silo);
            var before = stock.Volume;

            // A cleanbot has no hands, so activating the silo would mop it.
            server.System<SharedInteractionSystem>()
                .InteractionActivate(bot, silo, checkCanInteract: false, checkUseDelay: false, checkAccess: false);

            Assert.That(stock.Volume, Is.EqualTo(before));
            Assert.That(stock.GetTotalPrototypeQuantity(UnlinkedReagent), Is.EqualTo(FixedPoint2.Zero),
                "No contaminants go into the tank.");
            Assert.That(botContents!.Volume, Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(botContents.GetTotalPrototypeQuantity(Water), Is.EqualTo(FixedPoint2.Zero),
                "No water comes out of the tank.");

            entities.DeleteEntity(silo);
            entities.DeleteEntity(bot);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeconstructingThePartsSiloDropsItsParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid silo = default;
        var parts = new List<EntityUid>();

        await server.WaitAssertion(() =>
        {
            silo = entities.SpawnEntity("WGMachinePartsSilo", map.GridCoords);
            var store = entities.GetComponent<FabricationSiloComponent>(silo).Parts!;
            for (var i = 0; i < 3; i++)
            {
                var part = entities.SpawnEntity("ElectromagnetEconomy1", map.GridCoords);
                Assert.That(server.System<ContainerSystem>().Insert(part, store), Is.True);
                parts.Add(part);
            }

            // What the machine graph's deconstruct edge does.
            entities.EventBus.RaiseLocalEvent(silo, new MachineDeconstructedEvent());
            Assert.That(server.System<ConstructionSystem>().ChangeNode(silo, null, "machineFrame"), Is.True);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(silo), Is.True);
            foreach (var part in parts)
            {
                Assert.That(entities.Deleted(part), Is.False, "Stored parts survive deconstruction.");
                Assert.That(server.System<ContainerSystem>().IsEntityInContainer(part), Is.False,
                    "Stored parts drop at the silo.");
                entities.DeleteEntity(part);
            }

            var frames = new List<EntityUid>();
            var frameQuery = entities.EntityQueryEnumerator<MachineFrameComponent>();
            while (frameQuery.MoveNext(out var frame, out _))
            {
                frames.Add(frame);
            }

            foreach (var frame in frames)
            {
                entities.DeleteEntity(frame);
            }
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RemovingTheSiloDoesNotReact(bool destroy)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid silo = default;
        EntityUid store = default;
        EntityUid beaker = default;
        var puddles = 0;

        await server.WaitAssertion(() =>
        {
            silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            beaker = entities.SpawnEntity("Beaker", map.GridCoords);
            puddles = entities.Count<PuddleComponent>();

            // Silicon, potassium and nitrogen react to dylovene anywhere that lets them.
            AddToSilo(server, silo, new Solution("Silicon", 10));
            AddToSilo(server, silo, new Solution("Potassium", 10));
            AddToSilo(server, silo, new Solution("Nitrogen", 10));
            var silos = server.System<FabricationSiloSystem>();
            Assert.That(silos.TryGetSolution((silo, entities.GetComponent<FabricationSiloComponent>(silo)), out var soln, out _),
                Is.True);
            store = soln!.Value.Owner;
            Assert.That(server.System<ItemSlotsSystem>().TryInsert(silo, FabricationSiloComponent.ContainerSlotId, beaker, null),
                Is.True);

            if (destroy)
            {
                server.System<DestructibleSystem>().DestroyEntity(silo);
            }
            else
            {
                // What the machine graph's deconstruct edge does.
                entities.EventBus.RaiseLocalEvent(silo, new MachineDeconstructedEvent());
                Assert.That(server.System<ConstructionSystem>().ChangeNode(silo, null, "machineFrame"), Is.True);
            }
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(silo), Is.True);
            Assert.That(entities.Deleted(store), Is.True, "The stored solution is deleted with the silo.");
            Assert.That(entities.Count<PuddleComponent>(), Is.EqualTo(puddles), "Nothing spills.");

            var query = entities.EntityQueryEnumerator<SolutionComponent>();
            while (query.MoveNext(out _, out var solution))
            {
                Assert.That(solution.Solution.GetTotalPrototypeQuantity("Dylovene"), Is.EqualTo(FixedPoint2.Zero),
                    "The stored reagents never meet outside the silo.");
            }

            Assert.That(entities.Deleted(beaker), Is.False, "The slotted beaker drops out.");
            Assert.That(server.System<ContainerSystem>().IsEntityInContainer(beaker), Is.False);

            entities.DeleteEntity(beaker);
            var frames = new List<EntityUid>();
            var frameQuery = entities.EntityQueryEnumerator<MachineFrameComponent>();
            while (frameQuery.MoveNext(out var frame, out _))
            {
                frames.Add(frame);
            }

            foreach (var frame in frames)
            {
                entities.DeleteEntity(frame);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WhitelistComesFromLinkableLatheRecipes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var reagents = new HashSet<ProtoId<ReagentPrototype>>();
        var parts = new HashSet<EntProtoId>();
        await server.WaitAssertion(() =>
        {
            var silos = server.System<FabricationSiloSystem>();

            // Only the test recipes use these, so the test prototypes alone decide whether they are accepted.
            AssertOnlyUsedBy(server, "WGTestSiloRecipe", TestPart, TestReagent);
            AssertOnlyUsedBy(server, "WGTestUnlinkedRecipe", UnlinkedPart, UnlinkedReagent);
            Assert.That(silos.IsAcceptedPart(TestPart), Is.True, "A part used by a linkable lathe's recipe is accepted.");
            Assert.That(silos.IsAcceptedReagent(TestReagent), Is.True);
            Assert.That(silos.IsAcceptedPart(UnlinkedPart), Is.False, "A lathe that cannot link to a silo adds nothing.");
            Assert.That(silos.IsAcceptedReagent(UnlinkedReagent), Is.False);

            var recipe = server.ProtoMan.Index(AltCapacitorRecipe);
            Assert.That(recipe.Entities, Is.Not.Empty);
            Assert.That(recipe.Reagents, Is.Not.Empty);
            foreach (var part in recipe.Entities.Keys)
            {
                Assert.That(silos.IsAcceptedPart(part), Is.True);
            }

            foreach (var reagent in recipe.Reagents.Keys)
            {
                Assert.That(silos.IsAcceptedReagent(reagent), Is.True);
            }

            Assert.That(silos.IsAcceptedReagent(Water), Is.False, "No fabrication recipe uses water.");
            Assert.That(silos.IsAcceptedPart((EntProtoId) "Beaker"), Is.False);

            reagents.UnionWith(silos.AcceptedReagents);
            parts.UnionWith(silos.AcceptedParts);
        });

        // The client derives the same lists from its own prototypes.
        await pair.Client.WaitAssertion(() =>
        {
            var silos = pair.Client.System<SharedFabricationSiloSystem>();
            Assert.That(silos.AcceptedReagents, Is.EquivalentTo(reagents));
            Assert.That(silos.AcceptedParts, Is.EquivalentTo(parts));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MixedPourMovesOnlyAcceptedReagents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var beaker = entities.SpawnEntity("Beaker", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var solutions = server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetDrainableSolution(beaker, out var soln, out var contents), Is.True);
            Assert.That(solutions.TryAddReagent(soln!.Value, Silicon, 10), Is.True);
            Assert.That(solutions.TryAddReagent(soln.Value, Superconductant, 10), Is.True);
            Assert.That(solutions.TryAddReagent(soln.Value, Water, 10), Is.True);
            var transfer = entities.GetComponent<SolutionTransferComponent>(beaker);
            transfer.TransferAmount = 10;

            Assert.That(Pour(server, user, beaker, silo, map.GridCoords), Is.True);
            var stock = SiloSolution(server, silo);
            Assert.That(stock.Volume, Is.EqualTo(FixedPoint2.New(10)), "The transfer amount limits the pour.");
            Assert.That(stock.GetTotalPrototypeQuantity(Silicon), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(stock.GetTotalPrototypeQuantity(Superconductant), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(stock.GetTotalPrototypeQuantity(Water), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(contents!.GetTotalPrototypeQuantity(Water), Is.EqualTo(FixedPoint2.New(10)));

            transfer.TransferAmount = 50;
            Pour(server, user, beaker, silo, map.GridCoords);
            Assert.That(stock.Volume, Is.EqualTo(FixedPoint2.New(20)), "A larger pour takes every accepted reagent.");
            Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(10)), "The refused water stays in the beaker.");
            Assert.That(contents.GetTotalPrototypeQuantity(Water), Is.EqualTo(FixedPoint2.New(10)));

            Pour(server, user, beaker, silo, map.GridCoords);
            Assert.That(stock.Volume, Is.EqualTo(FixedPoint2.New(20)), "Nothing moves when nothing is accepted.");
            Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(10)));

            entities.DeleteEntity(beaker);
            entities.DeleteEntity(silo);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SyringeInjectsOnlyAcceptedReagents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var syringe = entities.SpawnEntity("Syringe", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var solutions = server.System<SharedSolutionContainerSystem>();
            var injector = entities.GetComponent<InjectorComponent>(syringe);
            server.System<SharedInjectorSystem>().SetMode((syringe, injector), InjectorToggleMode.Inject);
            Assert.That(solutions.TryGetSolution(syringe, injector.SolutionName, out var soln, out var contents), Is.True);
            Assert.That(solutions.TryAddReagent(soln!.Value, Water, 5), Is.True);

            Assert.That(Pour(server, user, syringe, silo, map.GridCoords), Is.True);
            var stock = SiloSolution(server, silo);
            Assert.That(stock.Volume, Is.EqualTo(FixedPoint2.Zero), "A refused reagent is not injected.");
            Assert.That(contents!.GetTotalPrototypeQuantity(Water), Is.EqualTo(FixedPoint2.New(5)));

            solutions.RemoveAllSolution(soln.Value);
            Assert.That(solutions.TryAddReagent(soln.Value, Silicon, 5), Is.True);
            Assert.That(Pour(server, user, syringe, silo, map.GridCoords), Is.True);
            Assert.That(stock.GetTotalPrototypeQuantity(Silicon), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(injector.ToggleState, Is.EqualTo(InjectorToggleMode.Draw), "An emptied syringe switches to draw.");

            entities.DeleteEntity(syringe);
            entities.DeleteEntity(silo);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StandardTransferRefusesUnacceptedReagents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var mixed = entities.SpawnEntity("Beaker", map.GridCoords);
            var clean = entities.SpawnEntity("Beaker", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var solutions = server.System<SharedSolutionContainerSystem>();
            var transfer = server.System<SolutionTransferSystem>();
            Assert.That(server.System<FabricationSiloSystem>()
                    .TryGetSolution((silo, entities.GetComponent<FabricationSiloComponent>(silo)), out var siloSoln, out var stock),
                Is.True);
            Assert.That(solutions.TryGetDrainableSolution(mixed, out var mixedSoln, out _), Is.True);
            Assert.That(solutions.TryAddReagent(mixedSoln!.Value, Silicon, 10), Is.True);
            Assert.That(solutions.TryAddReagent(mixedSoln.Value, Water, 10), Is.True);
            Assert.That(solutions.TryGetDrainableSolution(clean, out var cleanSoln, out _), Is.True);
            Assert.That(solutions.TryAddReagent(cleanSoln!.Value, Silicon, 10), Is.True);

            // Other systems calling the standard transfer cannot split a mixture, so the silo cancels it.
            Assert.That(transfer.Transfer(user, mixed, mixedSoln.Value, silo, siloSoln!.Value, 20), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(stock!.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(transfer.Transfer(user, clean, cleanSoln.Value, silo, siloSoln.Value, 20), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(stock.GetTotalPrototypeQuantity(Silicon), Is.EqualTo(FixedPoint2.New(10)));

            entities.DeleteEntity(mixed);
            entities.DeleteEntity(clean);
            entities.DeleteEntity(silo);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DumpLoadsOnlyAcceptedParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var silo = entities.SpawnEntity("WGMachinePartsSilo", map.GridCoords);
            var chemicals = entities.SpawnEntity("WGMachineChemicalSilo", map.GridCoords);
            var bag = entities.SpawnEntity("ClothingBackpack", map.GridCoords);
            var part = entities.SpawnEntity("CapacitorEconomy1", map.GridCoords);
            var other = entities.SpawnEntity("Beaker", map.GridCoords);
            var user = entities.SpawnEntity(null, map.GridCoords);
            var storage = server.System<SharedStorageSystem>();
            Assert.That(storage.Insert(bag, part, out _, playSound: false), Is.True);
            Assert.That(storage.Insert(bag, other, out _, playSound: false), Is.True);

            var verb = new GetDumpableVerbEvent(user, null);
            entities.EventBus.RaiseLocalEvent(silo, ref verb);
            Assert.That(verb.Verb, Is.Not.Null, "A bag can be dumped into a parts silo.");
            var chemicalVerb = new GetDumpableVerbEvent(user, null);
            entities.EventBus.RaiseLocalEvent(chemicals, ref chemicalVerb);
            Assert.That(chemicalVerb.Verb, Is.Null);

            // Using the bag on the silo leaves it to the dump verb instead of refusing it.
            Assert.That(Pour(server, user, bag, silo, map.GridCoords), Is.False);

            var contents = entities.GetComponent<StorageComponent>(bag).Container;
            var dump = new DumpEvent(new Queue<EntityUid>(contents.ContainedEntities), user, false, false);
            entities.EventBus.RaiseLocalEvent(silo, ref dump);
            Assert.That(dump.Handled, Is.True);
            var stored = entities.GetComponent<FabricationSiloComponent>(silo).Parts;
            Assert.That(stored!.Contains(part), Is.True);
            Assert.That(stored.Contains(other), Is.False);
            Assert.That(contents.Contains(other), Is.True, "What the silo refuses stays in the bag.");

            entities.DeleteEntity(bag);
            entities.DeleteEntity(silo);
            entities.DeleteEntity(chemicals);
            entities.DeleteEntity(user);
        });
        await pair.CleanReturnAsync();
    }

    private static void AssertOnlyUsedBy(RobustIntegrationTest.ServerIntegrationInstance server,
        string recipe,
        EntProtoId part,
        string reagent)
    {
        var users = server.ProtoMan.EnumeratePrototypes<LatheRecipePrototype>()
            .Where(r => r.Entities.ContainsKey(part) || r.Reagents.ContainsKey(reagent))
            .Select(r => r.ID);
        Assert.That(users, Is.EquivalentTo(new[] { recipe }));
    }

    private static bool Pour(RobustIntegrationTest.ServerIntegrationInstance server,
        EntityUid user,
        EntityUid used,
        EntityUid target,
        EntityCoordinates coordinates)
    {
        return server.System<SharedInteractionSystem>()
            .InteractUsing(user, used, target, coordinates, checkCanInteract: false, checkCanUse: false);
    }

    private static void Power(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid uid)
    {
        server.System<SharedPowerReceiverSystem>().SetNeedsPower(uid, false);
        server.EntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
    }

    private static void Toggle(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid silo, EntityUid client)
    {
        server.EntMan.EventBus.RaiseLocalEvent(silo, new ToggleOreSiloClientMessage(server.EntMan.GetNetEntity(client)));
    }

    private static void AddToSilo(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid silo, Solution solution)
    {
        var silos = server.System<FabricationSiloSystem>();
        Assert.That(silos.TryGetSolution((silo, server.EntMan.GetComponent<FabricationSiloComponent>(silo)), out var soln, out _),
            Is.True);
        Assert.That(server.System<SharedSolutionContainerSystem>().TryAddSolution(soln!.Value, solution), Is.True);
    }

    private static Solution SiloSolution(RobustIntegrationTest.ServerIntegrationInstance server, EntityUid silo)
    {
        var silos = server.System<FabricationSiloSystem>();
        Assert.That(silos.TryGetSolution((silo, server.EntMan.GetComponent<FabricationSiloComponent>(silo)), out _, out var solution),
            Is.True);
        return solution!;
    }
}
