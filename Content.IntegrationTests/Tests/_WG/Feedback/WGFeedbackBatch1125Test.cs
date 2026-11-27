#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WG.Wolfmed;
using Content.Server._WG.Wolfmed.Life;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._WG.Bench;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Drunk;
using Content.Shared.FixedPoint;
using Content.Shared.Item;
using Content.Shared.StatusEffect;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Bug reports from the 2026-11-25 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1125Test : WolfmedGameTest
{
    private static readonly EntProtoId MobileDrydockConsole = "WGComputerMobileDrydock";

    /// <summary>A fentanyl crystal takes 25 u; less waits in the beaker instead of turning to dylovene.</summary>
    [Test]
    public async Task FentanylCrystalNeedsTheWholeBatchTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var beaker = SEntMan.SpawnEntity("LargeBeaker", map.GridCoords);
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out var soln, out var contents));
            solutions.TryAddReagent(soln!.Value, "Fentanyl", FixedPoint2.New(5));
            solutions.TryAddReagent(soln.Value, "Tricordrazine", FixedPoint2.New(1));
            solutions.SetTemperature(soln.Value, 320f);

            Assert.Multiple(() =>
            {
                Assert.That(contents!.GetTotalPrototypeQuantity("Fentanyl"), Is.EqualTo(FixedPoint2.New(5)),
                    "5 u of fentanyl reacted for no crystal.");
                Assert.That(contents.GetTotalPrototypeQuantity("Dylovene"), Is.EqualTo(FixedPoint2.Zero));
            });

            solutions.TryAddReagent(soln.Value, "Fentanyl", FixedPoint2.New(20));
            solutions.SetTemperature(soln.Value, 320f);
            Assert.Multiple(() =>
            {
                Assert.That(contents!.GetTotalPrototypeQuantity("Fentanyl"), Is.EqualTo(FixedPoint2.Zero));
                Assert.That(SEntMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "FentanylSolidified"),
                    Is.EqualTo(1), "25 u of fentanyl made no crystal.");
            });
        });
    }

    /// <summary>
    /// A moth's stomach clears ethanol slowly enough that it gets about as drunk as a human on the same drink.
    /// </summary>
    [Test]
    public async Task MothGetsDrunkTest()
    {
        var map = await CreateTestMap();
        EntityUid moth = default, human = default;

        await Server.WaitPost(() =>
        {
            moth = SEntMan.SpawnEntity("MobMoth", map.GridCoords);
            human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            foreach (var body in new[] { moth, human })
            {
                var stomach = SEntMan.System<SharedBodySystem>().GetBodyOrganEntityComps<StomachComponent>(body).First().Owner;
                Assert.That(SEntMan.System<StomachSystem>().TryTransferSolution(stomach, new Solution("Moonshine", FixedPoint2.New(30))));
            }
        });

        await RunSeconds(60);

        await Server.WaitAssertion(() =>
        {
            var status = SEntMan.System<StatusEffectsSystem>();
            var timing = Server.ResolveDependency<IGameTiming>();

            double Left(EntityUid mob) => status.TryGetTime(mob, SharedDrunkSystem.DrunkKey, out var time)
                ? (time.Value.Item2 - timing.CurTime).TotalSeconds
                : 0;

            Assert.That(Left(human), Is.GreaterThan(0), "30 u of moonshine left a human sober.");
            Assert.That(Left(moth), Is.GreaterThan(Left(human) * 0.5),
                $"a moth is far less drunk than a human ({Left(moth):0} s against {Left(human):0} s).");
        });
    }

    /// <summary>
    /// The brain's cold protection reads against the species' normal, so an Avali at its normal 261 K is not
    /// cryo-cold and its arrest clock runs about as a human's.
    /// </summary>
    [Test]
    public async Task AvaliBrainClockRunsTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var life = SEntMan.System<WolfmedLifeSystem>();
            var avali = SEntMan.SpawnEntity("MobAvali", map.GridCoords);
            var human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(life.StartArrest(avali, "test"), Is.True);
            Assert.That(life.StartArrest(human, "test"), Is.True);

            var avaliSeconds = life.GetBrainDeathSeconds(avali);
            var humanSeconds = life.GetBrainDeathSeconds(human);
            Assert.That(humanSeconds, Is.Not.Null);
            Assert.That(avaliSeconds, Is.Not.Null, "an Avali in arrest has no brain death coming within the hour.");
            Assert.That(avaliSeconds!.Value, Is.LessThan(humanSeconds!.Value * 2f),
                $"an Avali's brain keeps far longer than a human's ({avaliSeconds:0} s against {humanSeconds:0} s).");
        });
    }

    /// <summary>The mobile drydock console is in its ship's repair snapshot, so an SRD or drydock rebuilds it.</summary>
    [Test]
    public async Task MobileDrydockConsoleIsRepairableTest()
    {
        await Server.WaitAssertion(() =>
        {
            var proto = Server.ProtoMan.Index(MobileDrydockConsole);
            Assert.That(proto.Components.ContainsKey(SEntMan.ComponentFactory.GetComponentName<ShipRepairableComponent>()),
                "the mobile drydock console is left out of the repair snapshot.");
        });
    }

    /// <summary>Every law the Bench app lists is on the Common Writ page.</summary>
    [Test]
    public async Task CommonWritPageListsEveryLawTest()
    {
        await Server.WaitAssertion(() =>
        {
            var page = Server.ResolveDependency<IResourceManager>()
                .ContentFileReadText(new ResPath("/ServerInfo/_WG/Bench/Guidebook/CommonWrit.xml")).ReadToEnd();
            var loc = Server.ResolveDependency<ILocalizationManager>();
            Assert.Multiple(() =>
            {
                foreach (var law in Server.ProtoMan.EnumeratePrototypes<WGLawPrototype>())
                {
                    var name = loc.GetString(law.Name);
                    Assert.That(page, Does.Contain($"[bold]{name}[/bold]"), $"{law.ID} ({name}) is missing from the Common Writ page.");
                }
            });
        });
    }

    /// <summary>
    /// An in-hand layer with a map key gets the same key in both hands, and two held at once crash a Resomi client.
    /// No item may map its in-hand layers.
    /// </summary>
    [Test]
    public async Task NoInhandLayerIsMappedTest()
    {
        await Server.WaitAssertion(() =>
        {
            var name = SEntMan.ComponentFactory.GetComponentName<ItemComponent>();
            Assert.Multiple(() =>
            {
                foreach (var proto in Server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || !proto.Components.TryGetValue(name, out var entry))
                        continue;

                    var item = (ItemComponent) entry.Component;
                    foreach (var (hand, layers) in item.InhandVisuals)
                    {
                        Assert.That(layers.Any(layer => layer.MapKeys is { Count: > 0 }), Is.False,
                            $"{proto.ID} maps a {hand} in-hand layer.");
                    }
                }
            });
        });
    }
}
