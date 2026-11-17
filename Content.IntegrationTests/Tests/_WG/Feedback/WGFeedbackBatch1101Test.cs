#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WG.Wolfmed;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Construction;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Medical.CrewMonitoring;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Polymorph.Components;
using Content.Shared.Polymorph.Systems;
using Content.Shared.Research.Components;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Bug reports from the 2026-11-01 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1101Test : WolfmedGameTest
{
    private static readonly ProtoId<WoundPrototype> Systemic = "SystemicBleedingWound";

    /// <summary>Reports 367, 366: salt and water in a stomach stay apart and both move on to the blood.</summary>
    [Test]
    public async Task SaltAndWaterMakeNoSalineInABodyTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default, stomach = default;
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();

        await Server.WaitPost(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            stomach = SEntMan.System<SharedBodySystem>().GetBodyOrganEntityComps<StomachComponent>(body).First().Owner;
            var system = SEntMan.System<StomachSystem>();
            Assert.That(system.TryTransferSolution(stomach, new Solution("TableSalt", FixedPoint2.New(2))));
            Assert.That(system.TryTransferSolution(stomach, new Solution("Water", FixedPoint2.New(8))));
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(solutions.TryGetSolution(stomach, StomachSystem.DefaultSolutionName, out _, out var contents));
            Assert.That(contents!.GetTotalPrototypeQuantity("Saline"), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(contents.GetTotalPrototypeQuantity("TableSalt"), Is.EqualTo(FixedPoint2.New(2)));
        });
    }

    /// <summary>Report 367: anything in a stomach with no delta, as a reaction leaves it, still reaches the blood.</summary>
    [Test]
    public async Task UntrackedStomachReagentReachesTheBloodTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default, stomach = default;
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();

        await Server.WaitPost(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            stomach = SEntMan.System<SharedBodySystem>().GetBodyOrganEntityComps<StomachComponent>(body).First().Owner;
            Assert.That(solutions.TryGetSolution(stomach, StomachSystem.DefaultSolutionName, out var soln, out _));
            solutions.TryAddReagent(soln!.Value, "Saline", FixedPoint2.New(5));
        });

        var comp = SEntMan.GetComponent<StomachComponent>(stomach);
        var chemicals = SEntMan.GetComponent<BloodstreamComponent>(body).ChemicalSolutionName;
        await RunSeconds((float) comp.DigestionDelay.TotalSeconds - 1f);

        // The blood starts using it straight away, so look on every tick around the transfer.
        var delivered = false;
        for (var tick = 0; tick < 150 && !delivered; tick++)
        {
            await Server.WaitRunTicks(1);
            await Server.WaitPost(() =>
            {
                Assert.That(solutions.TryGetSolution(body, chemicals, out _, out var blood));
                delivered = blood!.GetTotalPrototypeQuantity("Saline") > FixedPoint2.Zero;
            });
        }

        await Server.WaitAssertion(() =>
        {
            Assert.That(delivered, "the saline never reached the blood.");
            Assert.That(solutions.TryGetSolution(stomach, StomachSystem.DefaultSolutionName, out _, out var contents));
            Assert.That(contents!.GetTotalPrototypeQuantity("Saline"), Is.EqualTo(FixedPoint2.Zero),
                "the saline never left the stomach.");
        });
    }

    /// <summary>Reports 366, 358: a systemic bleed that stops takes its wound with it, however it stopped.</summary>
    [Test]
    public async Task StoppedSystemicBleedLeavesNoWoundTest()
    {
        var map = await CreateTestMap();
        EntityUid body = default;
        WoundSystem wounds = default!;
        WoundBleedingSystem bleeding = default!;

        bool HasSystemic() => SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .SelectMany(part => wounds.GetWounds(part.Id))
            .Any(wound => wound.Comp.Prototype == Systemic);

        await Server.WaitPost(() =>
        {
            wounds = SEntMan.System<WoundSystem>();
            bleeding = SEntMan.System<WoundBleedingSystem>();
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(bleeding.ApplyReagentBleeding(body, 2f));
            Assert.That(HasSystemic());
            Assert.That(bleeding.StopBodyBleeding(body));
            Assert.That(HasSystemic(), Is.False, "a bleed stopped by treatment left its wound.");

            Assert.That(bleeding.ApplyReagentBleeding(body, 2f));
            var wound = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
                .SelectMany(part => wounds.GetWounds(part.Id))
                .First(found => found.Comp.Prototype == Systemic);
            var comp = SEntMan.GetComponent<WoundBleedingComponent>(wound);
            comp.AutomaticClottingAt = TimeSpan.Zero;
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(HasSystemic(), Is.False, "a clotted bleed left its wound.");
            Assert.That(SEntMan.GetComponent<BloodstreamComponent>(body).BleedAmount, Is.EqualTo(0f));
        });
    }

    /// <summary>Report 360: crafting leaves the worn jumpsuit alone when none is held.</summary>
    [Test]
    public async Task CraftingLeavesTheWornJumpsuitTest()
    {
        var map = await CreateTestMap();
        EntityUid user = default, worn = default;

        await Server.WaitPost(() =>
        {
            user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            worn = SEntMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", map.GridCoords);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(user, worn, "jumpsuit", silent: true, force: true));
        });
        await RunSeconds(1);

#pragma warning disable CS4014 // Legacy construction code awaits its own do-after.
        await Server.WaitPost(() => SEntMan.System<ConstructionSystem>().TryStartItemConstruction("WGWolfmedMakeshiftTourniquet", user));
#pragma warning restore CS4014
        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(worn), Is.False, "crafting tore the worn jumpsuit.");
            Assert.That(SEntMan.System<InventorySystem>().TryGetSlotEntity(user, "jumpsuit", out var still) && still == worn);
        });
    }

    /// <summary>Report 356: dropping a disguise after anchoring gives the mob its walking body back.</summary>
    [Test]
    public async Task ChameleonAnchorLeavesTheMobAbleToWalkTest()
    {
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var projectors = SEntMan.System<SharedChameleonProjectorSystem>();
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var projector = SEntMan.SpawnEntity("ChameleonProjector", map.GridCoords);
            var target = SEntMan.SpawnEntity("CrateGeneric", map.GridCoords);
            var physics = SEntMan.GetComponent<PhysicsComponent>(user);
            Assert.That(physics.BodyType, Is.EqualTo(BodyType.KinematicController));

            projectors.Disguise((projector, SEntMan.GetComponent<ChameleonProjectorComponent>(projector)), user, target);
            var ev = new DisguiseToggleAnchoredEvent();
            SEntMan.EventBus.RaiseLocalEvent(projector, ev);
            Assert.That(SEntMan.GetComponent<TransformComponent>(user).Anchored, "the anchor action did nothing.");

            ev = new DisguiseToggleAnchoredEvent();
            SEntMan.EventBus.RaiseLocalEvent(projector, ev);
            Assert.That(SEntMan.GetComponent<TransformComponent>(user).Anchored, Is.False);
            Assert.That(physics.BodyType, Is.EqualTo(BodyType.KinematicController), "toggling off left a Dynamic mob.");

            ev = new DisguiseToggleAnchoredEvent();
            SEntMan.EventBus.RaiseLocalEvent(projector, ev);
            Assert.That(projectors.TryReveal(user));
            Assert.That(physics.BodyType, Is.EqualTo(BodyType.KinematicController), "revealing left a Dynamic mob.");

            // Going into a container unanchors without touching the body, then reveals.
            projectors.Disguise((projector, SEntMan.GetComponent<ChameleonProjectorComponent>(projector)), user, target);
            ev = new DisguiseToggleAnchoredEvent();
            SEntMan.EventBus.RaiseLocalEvent(projector, ev);
            Assert.That(physics.BodyType, Is.EqualTo(BodyType.Static));
            var containers = SEntMan.System<SharedContainerSystem>();
            var holder = SEntMan.SpawnEntity(null, map.GridCoords);
            Assert.That(containers.Insert(user, containers.EnsureContainer<Container>(holder, "wg-test")));
            Assert.That(SEntMan.HasComponent<ChameleonDisguisedComponent>(user), Is.False);
            Assert.That(physics.BodyType, Is.EqualTo(BodyType.KinematicController), "a container left the mob anchored in body.");
        });
    }

    /// <summary>Reports 370, 369: a research client moved to another grid drops its server and finds that grid's.</summary>
    [Test]
    public async Task ResearchClientFollowsItsGridTest()
    {
        var home = await CreateTestMap();
        var away = await CreateTestMap();
        var plain = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var xforms = SEntMan.System<SharedTransformSystem>();
            var homeServer = SEntMan.SpawnAtPosition("ResearchAndDevelopmentServer", home.GridCoords);
            var awayServer = SEntMan.SpawnAtPosition("ResearchAndDevelopmentServer", away.GridCoords);
            var client = SEntMan.SpawnAtPosition("ComputerResearchAndDevelopment", home.GridCoords);
            var comp = SEntMan.GetComponent<ResearchClientComponent>(client);
            Assert.That(comp.Server, Is.EqualTo(homeServer));

            xforms.SetCoordinates(client, plain.GridCoords);
            Assert.That(comp.Server, Is.Null, "the client kept a server on the grid it left.");

            xforms.SetCoordinates(client, away.GridCoords);
            Assert.That(comp.Server, Is.EqualTo(awayServer));

            xforms.SetCoordinates(awayServer, plain.GridCoords);
            Assert.That(comp.Server, Is.Null, "a server that left kept its client.");

            var builtLater = SEntMan.SpawnAtPosition("ResearchAndDevelopmentServer", away.GridCoords);
            Assert.That(comp.Server, Is.EqualTo(builtLater), "a server built on the grid did not take the unlinked client.");
        });
    }

    /// <summary>Report 355: a crew server on another map does not take over from the active one.</summary>
    [Test]
    public async Task OnlyOneCrewServerRunsAcrossMapsTest()
    {
        var map = await CreateTestMap();
        var other = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var singleton = SEntMan.System<SingletonDeviceNetServerSystem>();
            var here = SEntMan.SpawnAtPosition("CrewMonitoringServer", map.GridCoords);
            var there = SEntMan.SpawnAtPosition("CrewMonitoringServer", other.GridCoords);

            Assert.That(singleton.TryGetActiveServerAddress<CrewMonitoringServerComponent>(map.MapId, out var first));
            Assert.That(singleton.TryGetActiveServerAddress<CrewMonitoringServerComponent>(other.MapId, out var second));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(singleton.IsActiveServer(here));
            Assert.That(singleton.IsActiveServer(there), Is.False);
        });
    }
}
