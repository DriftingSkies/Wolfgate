#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Medical;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Medical;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// A body with no lungs has a bridge: a ventilator with gas in its tank, or an autodoc sealed around it, stops the
/// brain's breathing drain, and the analyzer says so and gives the order to work in.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedVentilatorSystem))]
public sealed class WolfmedVentilatorTest : WolfmedGameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedTestVentilatorPod
  parent: WFMachineAutodoc
  suffix: test ventilation
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>A human with the lungs taken out, once the respirator has noticed.</summary>
    private async Task<EntityUid> Lungless(WolfmedScenario s, EntityCoordinates coords)
    {
        EntityUid body = default;
        await Server.WaitAssertion(() =>
        {
            body = SEntMan.SpawnEntity("MobHuman", coords);
            var graph = SEntMan.System<SharedBodySystem>();
            foreach (var lung in graph.GetBodyOrganEntityComps<LungComponent>((body, null)).ToList())
                Assert.That(graph.RemoveOrgan(lung.Owner), Is.True);
        });

        Assert.That(await SecondsUntil(30, () => s.Breathing.IsSuffocating(body)), Is.True,
            "the respirator never ran short.");
        return body;
    }

    private async Task<bool> SecondsUntil(int limit, System.Func<bool> condition)
    {
        for (var second = 0; second < limit; second++)
        {
            await RunSeconds(1);
            var met = false;
            await Server.WaitPost(() => met = condition());
            if (met)
                return true;
        }

        return false;
    }

    private float Drain(WolfmedScenario s, EntityUid body) => s.Life.DrainRate(body, s.Life.GetBrain(body)!.Value);

    [Test]
    public async Task VentilatorCarriesLunglessBodyTest()
    {
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var body = await Lungless(s, map.GridCoords);
        var ventilators = SEntMan.System<WolfmedVentilatorSystem>();
        Entity<WolfmedVentilatorComponent> stand = default;
        WolfmedVentilatorComponent state = default!;
        EntityUid tank = default;

        await Server.WaitAssertion(() =>
        {
            s.KeepGrid(map.Grid);
            Assert.Multiple(() =>
            {
                Assert.That(Drain(s, body), Is.GreaterThan(0f), "no lungs does not drain the brain.");
                Assert.That(s.Breathing.Assess(body),
                    Is.EqualTo((WolfmedBreathing.None, WolfmedBreathingSource.Lungs)));
                Assert.That(s.Analyzer(body), Does.Contain(Loc.GetString("wolfmed-vitals-aid-nolungs")));
            });

            // The mask on with no tank: the stand alarms and carries nobody.
            var uid = SEntMan.SpawnEntity("WFWolfmedVentilator", map.GridCoords);
            state = SEntMan.GetComponent<WolfmedVentilatorComponent>(uid);
            stand = (uid, state);
            ventilators.Attach(stand, body);
        });

        await RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(state.Patient, Is.EqualTo(body));
                Assert.That(state.DryBreaths, Is.GreaterThan(0), "a stand with no tank never alarmed.");
                Assert.That(s.Breathing.IsVentilated(body), Is.False);
                Assert.That(Drain(s, body), Is.GreaterThan(0f));
            });

            tank = SEntMan.SpawnEntity("OxygenTankFilled", map.GridCoords);
            Assert.That(SEntMan.System<ItemSlotsSystem>()
                .TryInsert(stand, WolfmedVentilatorComponent.TankSlotId, tank, null), Is.True);
        });

        var before = 0f;
        await Server.WaitPost(() => before = ventilators.GetTank(stand)!.Value.Comp.Air.TotalMoles);
        Assert.That(await SecondsUntil(6, () => s.Breathing.IsVentilated(body)), Is.True,
            "a stand with a full tank never ventilated its patient.");

        await RunSeconds(4);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(ventilators.GetTank(stand)!.Value.Comp.Air.TotalMoles, Is.LessThan(before),
                    "the stand breathes for free.");
                Assert.That(state.Supplying, Is.True);
                Assert.That(s.Breathing.IsVentilated(body), Is.True);
                Assert.That(Drain(s, body), Is.EqualTo(0f), "a ventilated body still drains its brain.");
                Assert.That(s.Breathing.Assess(body),
                    Is.EqualTo((WolfmedBreathing.Ventilated, WolfmedBreathingSource.Lungs)));

                var analyzer = s.Analyzer(body);
                Assert.That(analyzer, Does.Contain(Loc.GetString("wolfmed-vitals-breathing-ventilated-lungs")));
                Assert.That(analyzer, Does.Contain(Loc.GetString("wolfmed-vitals-aid-nolungs-ventilated")));
            });

            // Dragged out of reach: the mask comes off and the drain is back.
            SEntMan.System<SharedTransformSystem>().SetCoordinates(body, map.GridCoords.Offset(new Vector2(4f, 0f)));
        });

        await RunSeconds(1);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(state.Patient, Is.Null, "the mask stayed on a patient four tiles off.");
                Assert.That(s.Breathing.IsVentilated(body), Is.False);
                Assert.That(Drain(s, body), Is.GreaterThan(0f));
            });
        });
    }

    /// <summary>A second stand takes the patient over from the first, and its own detach then ends the ventilation.</summary>
    [Test]
    public async Task SecondStandTakesOverTest()
    {
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var ventilators = SEntMan.System<WolfmedVentilatorSystem>();
        var slots = SEntMan.System<ItemSlotsSystem>();
        EntityUid body = default;
        WolfmedVentilatorComponent first = default!, second = default!;
        EntityUid firstUid = default, secondUid = default;

        await Server.WaitAssertion(() =>
        {
            s.KeepGrid(map.Grid);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            firstUid = SEntMan.SpawnEntity("WFWolfmedVentilator", map.GridCoords);
            secondUid = SEntMan.SpawnEntity("WFWolfmedVentilator", map.GridCoords);
            first = SEntMan.GetComponent<WolfmedVentilatorComponent>(firstUid);
            second = SEntMan.GetComponent<WolfmedVentilatorComponent>(secondUid);
            foreach (var stand in new[] { firstUid, secondUid })
            {
                var tank = SEntMan.SpawnEntity("OxygenTankFilled", map.GridCoords);
                Assert.That(slots.TryInsert(stand, WolfmedVentilatorComponent.TankSlotId, tank, null), Is.True);
            }

            ventilators.Attach((firstUid, first), body);
        });

        Assert.That(await SecondsUntil(6, () => s.Breathing.IsVentilated(body)), Is.True);
        await Server.WaitAssertion(() =>
        {
            ventilators.Attach((secondUid, second), body);
            Assert.Multiple(() =>
            {
                Assert.That(first.Patient, Is.Null, "two stands hold one patient.");
                Assert.That(second.Patient, Is.EqualTo(body));
            });
        });

        Assert.That(await SecondsUntil(6, () => s.Breathing.IsVentilated(body)), Is.True);
        await Server.WaitAssertion(() =>
        {
            ventilators.Detach((secondUid, second));
            Assert.That(s.Breathing.IsVentilated(body), Is.False);
        });
    }

    [Test]
    public async Task SealedAutodocVentilatesTest()
    {
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var body = await Lungless(s, map.GridCoords);
        var autodoc = SEntMan.System<AutodocSystem>();
        Entity<AutodocComponent> pod = default;

        await Server.WaitAssertion(() =>
        {
            s.KeepGrid(map.Grid);
            var uid = SEntMan.SpawnEntity("WolfmedTestVentilatorPod", map.GridCoords);
            pod = (uid, SEntMan.GetComponent<AutodocComponent>(uid));
            Assert.That(autodoc.TryInsert(pod, body), Is.True);
        });

        // The power net writes Powered on its own tick, so the pod is not sealed the instant it spawns.
        Assert.That(await SecondsUntil(5, () => autodoc.IsSealedIn(body)), Is.True, "the test pod never sealed.");
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(s.Breathing.IsVentilated(body), Is.True);
                Assert.That(Drain(s, body), Is.EqualTo(0f), "a sealed pod leaves a lungless brain draining.");
                Assert.That(s.Breathing.Assess(body),
                    Is.EqualTo((WolfmedBreathing.Ventilated, WolfmedBreathingSource.Lungs)));
            });

            Assert.That(autodoc.TryEject(pod, force: true), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(s.Breathing.IsVentilated(body), Is.False);
                Assert.That(Drain(s, body), Is.GreaterThan(0f));
            });
        });
    }

    /// <summary>The "Do first" line for a body with no lungs: what breathes for it comes before the shock.</summary>
    [Test]
    public async Task LunglessAdviceTest()
    {
        await Server.WaitAssertion(() =>
        {
            var arrest = new WolfmedVitalsReport
            {
                State = WolfmedVitalsState.Arrest,
                Blood = 1f,
                Lungless = true,
                Routes = WolfmedRoutes.Arrest,
            };
            var pulse = new WolfmedVitalsReport
            {
                State = WolfmedVitalsState.Unconscious,
                Blood = 1f,
                Lungless = true,
                Routes = WolfmedRoutes.Lungs,
            };
            var ventilated = new WolfmedVitalsReport
            {
                State = WolfmedVitalsState.Arrest,
                Blood = 1f,
                Lungless = true,
                Ventilated = true,
                Routes = WolfmedRoutes.Arrest,
            };
            var damaged = new WolfmedVitalsReport
            {
                State = WolfmedVitalsState.Downed,
                Blood = 1f,
                Routes = WolfmedRoutes.Lungs,
            };

            Assert.Multiple(() =>
            {
                Assert.That(WolfmedVitalsText.DoFirstLine(arrest),
                    Is.EqualTo("Do first: CPR, ventilator on, then the defibrillator; new lungs"));
                Assert.That(WolfmedVitalsText.DoFirstLine(pulse),
                    Is.EqualTo("Do first: ventilator, autodoc or CPR, then new lungs"));
                Assert.That(WolfmedVitalsText.DoFirstLine(ventilated),
                    Is.EqualTo("Do first: CPR, then the defibrillator; new lungs, keep them ventilated"));
                Assert.That(WolfmedVitalsText.DoFirstLine(damaged),
                    Is.EqualTo("Do first: ventilator, autodoc or CPR, then lung surgery"));
            });
        });
    }
}
