using Content.Shared.Chemistry.Reaction;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Mono.Cargo;
using Content.Server.Botany;
using Content.Server.Cargo.Systems;
using Content.Server.Nutrition.Components;
using Content.Shared._Mono.ItemTax.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Contraband;
using Content.Shared.FixedPoint;
using Content.Shared.Storage.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.WolfChem.Narcotics;

/// <summary>
/// Every new drug can be made from things players can get: plants, ground exotic matter and dispenser chemicals,
/// through real reactions at their optimum temperature.
/// </summary>
[TestFixture]
public sealed class SubstanceTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFSubstanceTestBeaker
  components:
  - type: SolutionContainerManager
    solutions:
      beaker:
        maxVol: 1000
";

    private sealed class Lab(RobustIntegrationTest.ServerIntegrationInstance server, EntityCoordinates coords)
    {
        private readonly SharedSolutionContainerSystem _solutions = server.System<SharedSolutionContainerSystem>();

        /// <summary>Pours <paramref name="inputs"/> into a fresh beaker at <paramref name="temperature"/> and lets it react.</summary>
        public Solution React(float temperature, Solution start, params (string Reagent, float Units)[] inputs)
        {
            var mix = start?.Clone() ?? new Solution();
            foreach (var (reagent, units) in inputs)
            {
                mix.AddReagent(reagent, FixedPoint2.New(units));
            }

            mix.Temperature = temperature;
            var beaker = server.EntMan.SpawnEntity("WFSubstanceTestBeaker", coords);
            _solutions.TryGetSolution(beaker, "beaker", out var soln, out var solution);
            _solutions.TryAddSolution(soln!.Value, mix);
            return solution!;
        }

        /// <summary>Spawned entities of a prototype, with their first solution.</summary>
        public List<Solution> Spawned(string prototype)
        {
            var found = new List<Solution>();
            var query = server.EntMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID != prototype)
                    continue;

                foreach (var (_, soln) in _solutions.EnumerateSolutions(uid))
                {
                    found.Add(soln.Comp.Solution);
                    break;
                }
            }

            return found;
        }

        public Solution Contents(string prototype, string solution)
        {
            var uid = server.EntMan.SpawnEntity(prototype, coords);
            _solutions.TryGetSolution(uid, solution, out _, out var contents);
            return contents!;
        }
    }

    [Test]
    public async Task ReachableTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            // A pooled pair keeps the optimum shifts its last round start rolled; the temperatures below are the unshifted optimums.
            server.System<ChemicalReactionSystem>().WFOptimumShift.Clear();
            var lab = new Lab(server, map.GridCoords);

            Assert.Multiple(() =>
            {
                // Inputs from the frontier and the garden.
                Assert.That(lab.Contents("WFLeavesCoca", "food").ContainsPrototype("WFCocaAlkaloid"), Is.True);
                Assert.That(lab.Contents("WFOpiumPoppyPod", "food").ContainsPrototype("WFOpium"), Is.True);
                Assert.That(lab.Contents("WFMaterialExoticMatter1", "exotic").ContainsPrototype("WFLatticeResidue"), Is.True,
                    "Exotic matter should grind to Lattice residue.");
                Assert.That(protos.Index<SeedPrototype>("poppy").MutationPrototypes, Does.Contain("WFOpiumPoppy"));

                var crate = protos.Index<EntityPrototype>("CrateHydroponicsSeedsExotic");
                Assert.That(crate.TryGetComponent<StorageFillComponent>(out var fill, server.EntMan.ComponentFactory), Is.True);
                var stock = fill!.Contents.Select(e => e.PrototypeId?.Id).ToList();
                Assert.That(stock, Does.Contain("WFCocaSeeds").And.Contain("WFOpiumPoppySeeds"));

                Assert.That(protos.Index<ConstructionGraphPrototype>("WFHash").TryPath("start", "brick", out _), Is.True);
                Assert.That(lab.Contents("WFHashBrick", "food").ContainsPrototype("THC"), Is.True);
            });

            var cocaine = lab.React(330f, null, ("WFCocaAlkaloid", 15), ("SodiumCarbonate", 5), ("Acetone", 5));
            var heroin = lab.React(350f, null, ("WFOpium", 10), ("Vinegar", 5), ("Ammonia", 5));
            var redlineBase = lab.React(360f, null, ("Ephedrine", 5), ("Phosphorus", 5), ("Plasma", 5));
            var redline = lab.React(300f, redlineBase, ("Epinephrine", 5), ("Iron", 5));
            var anvilBase = lab.React(340f, null, ("Bicaridine", 5), ("Silicon", 5), ("Lithium", 5));
            var anvil = lab.React(290f, anvilBase, ("Tramadol", 5), ("Mercury", 5));
            var haloBase = lab.React(310f, null, ("Happiness", 5), ("Oil", 5), ("Potassium", 5));
            var halo = lab.React(340f, haloBase, ("Ammonia", 5), ("Copper", 5));
            var chorusBase = lab.React(270f, null, ("WFLatticeResidue", 5), ("SpaceDrugs", 5), ("Ethanol", 5));
            var chorus = lab.React(310f, chorusBase, ("MindbreakerToxin", 5), ("Iodine", 5));

            Assert.Multiple(() =>
            {
                Assert.That(cocaine.GetTotalPrototypeQuantity("WFCocaine"), Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(cocaine.GetPurity("WFCocaine"), Is.EqualTo(1f).Within(0.01f), "The optimum gives full grade.");
                Assert.That(heroin.GetTotalPrototypeQuantity("WFHeroin"), Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(redline.GetTotalPrototypeQuantity("WFRedline"), Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(anvil.GetTotalPrototypeQuantity("WFAnvil"), Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(halo.GetTotalPrototypeQuantity("WFHalo"), Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(chorus.GetTotalPrototypeQuantity("WFChorus"), Is.EqualTo(FixedPoint2.New(15)));
            });

            // Solids carry the grade of the batch they set from.
            var cut = new Solution();
            cut.AddReagent("WFCocaine", FixedPoint2.New(10), 0.6f);
            lab.React(385f, cut, ("SodiumCarbonate", 2), ("Water", 2));
            var unset = lab.React(250f, null, ("WFOpium", 10));
            Assert.That(lab.Spawned("WFOpiumResin"), Is.Empty, "Chilled opium waits for water, so the cook picks the moment.");
            Assert.That(unset.GetTotalPrototypeQuantity("WFOpium"), Is.EqualTo(FixedPoint2.New(10)));

            lab.React(220f, null, ("Desoxyephedrine", 10), ("Acetone", 1));
            lab.React(250f, null, ("WFOpium", 10), ("Water", 1));

            var rocks = lab.Spawned("WFCrackRock");
            var shards = lab.Spawned("WFCrystalShard");
            var resin = lab.Spawned("WFOpiumResin");
            Assert.Multiple(() =>
            {
                Assert.That(rocks, Has.Count.EqualTo(2), "10 u of cocaine should set into two rocks.");
                Assert.That(rocks.Select(r => r.GetPurity("WFCrack")), Has.All.EqualTo(0.6f).Within(0.01f),
                    "A rock takes the grade of the cocaine; standard-grade reagents don't raise it.");
                Assert.That(shards, Has.Count.EqualTo(2));
                Assert.That(shards.Select(s => s.GetPurity("Desoxyephedrine")), Has.All.EqualTo(1f).Within(0.01f));
                Assert.That(resin, Has.Count.EqualTo(2), "10 u of chilled opium should set into two lumps of resin.");
                Assert.That(resin.Select(r => r.GetTotalPrototypeQuantity("WFOpium")),
                    Has.All.EqualTo(FixedPoint2.New(5)));
            });
        });

        await pair.CleanReturnAsync();
    }

    private static readonly string[] Products =
    {
        "WFCrackRock", "WFCrystalShard", "WFOpiumResin", "WFBaggieCocaine", "WFBaggieHeroin",
    };

    /// <summary>
    /// Products and programmed sticks are Class 3 contraband that tips off security when sold, hash and blanks are legal,
    /// and cut stock is worth less.
    /// </summary>
    [Test]
    public async Task ContrabandAndPriceTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var pricing = server.System<PricingSystem>();
            Assert.Multiple(() =>
            {
                foreach (var id in Products)
                {
                    var uid = entMan.SpawnEntity(id, map.GridCoords);
                    Assert.That(entMan.GetComponent<ContrabandComponent>(uid).Severity.Id, Is.EqualTo("Class3Chem"), id);
                    Assert.That(entMan.HasComponent<SoldAlertComponent>(uid), Is.True, id);
                    Assert.That(entMan.HasComponent<ItemTaxComponent>(uid), Is.True, id);
                }

                foreach (var id in new[] { "WFNeurowareStickWhiteline", "WFNeurowareStickWhitelineCopy" })
                {
                    var uid = entMan.SpawnEntity(id, map.GridCoords);
                    Assert.That(entMan.GetComponent<ContrabandComponent>(uid).Severity.Id, Is.EqualTo("Class3Chem"), id);
                    Assert.That(entMan.HasComponent<SoldAlertComponent>(uid), Is.True, id);
                    Assert.That(entMan.HasComponent<ItemTaxComponent>(uid), Is.True, id);
                }

                foreach (var id in new[] { "WFHashBrick", "WFNeurowareStickIdle", "WFNeurowareStickIdleCopy" })
                {
                    var uid = entMan.SpawnEntity(id, map.GridCoords);
                    Assert.That(entMan.HasComponent<ContrabandComponent>(uid), Is.False, $"{id} is legal.");
                    Assert.That(entMan.HasComponent<SoldAlertComponent>(uid), Is.False, $"Selling {id} tips nobody off.");
                }

                var blank = entMan.SpawnEntity("WFNeurowareStick", map.GridCoords);
                Assert.That(entMan.HasComponent<ContrabandComponent>(blank), Is.False, "A blank stick is legal.");
                Assert.That(pricing.GetPrice(entMan.SpawnEntity("WFNeurowareStickWhitelineCopy", map.GridCoords)),
                    Is.GreaterThan(pricing.GetPrice(blank)), "A copy sells above a blank.");

                var full = pricing.GetPrice(entMan.SpawnEntity("WFBaggieCocaine", map.GridCoords));
                var cut = pricing.GetPrice(entMan.SpawnEntity("WFBaggieCocaineCut", map.GridCoords));
                Assert.That(full, Is.GreaterThan(0));
                Assert.That(cut, Is.EqualTo(full * 0.55).Within(1), "Cut stock sells at its grade.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
