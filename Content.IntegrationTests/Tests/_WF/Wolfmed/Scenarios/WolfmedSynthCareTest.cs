#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Server._WF.Wolfmed.Medical;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Wolfmed.Medical;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Lathe;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.PowerCell;
using NUnit.Framework;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// A synth runs on hydraulic fluid, so the pack and the IV refill it, and its power cell comes out and goes back in
/// through surgery on the opened torso.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedFluidPackSystem))]
public sealed class WolfmedSynthCareTest : WolfmedGameTest
{
    public override PoolSettings PoolSettings => PsDisconnected;

    private const string Fluid = "WFWolfmedHydraulicFluid";
    private const string Pack = "WFWolfmedHydraulicFluidPack";

    private FixedPoint2 Volume(EntityUid uid, string solution) =>
        SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(uid, solution, out _, out var found)
            ? found.Volume
            : FixedPoint2.Zero;

    [Test]
    public async Task HydraulicFluidRefillsASynthTest()
    {
        var map = await CreateTestMap();
        EntityUid synth = default, medic = default, pack = default, dripPack = default;
        Entity<WolfmedIvDripComponent> drip = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<Content.Server._Mono.Cleanup.CleanupImmuneComponent>(map.Grid);
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            synth = SEntMan.SpawnEntity("MobSynth", map.GridCoords.Offset(new Vector2(1f, 0f)));
            medic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            pack = SEntMan.SpawnEntity(Pack, map.GridCoords);

            var bloodstream = SEntMan.GetComponent<BloodstreamComponent>(synth);
            Assert.That(bloodstream.BloodReagent.Id, Is.EqualTo(Fluid), "a synth does not run on hydraulic fluid.");
            SEntMan.System<BloodstreamSystem>().TryModifyBloodLevel(synth, FixedPoint2.New(-120));

            var packs = SEntMan.System<WolfmedFluidPackSystem>();
            var packEnt = (pack, SEntMan.GetComponent<WolfmedFluidPackComponent>(pack));
            Assert.That(packs.GetRefusal(packEnt, synth), Is.Null, "the hydraulic pack refuses a synth.");
            var before = Volume(synth, bloodstream.BloodSolutionName);
            Assert.That(packs.TryTransfer(packEnt, synth, medic, out var moved), Is.True);
            Assert.That(moved, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(Volume(synth, bloodstream.BloodSolutionName), Is.GreaterThan(before), "the pack did not refill the synth.");

            var dripUid = SEntMan.SpawnEntity("WFWolfmedIvDrip", map.GridCoords);
            drip = (dripUid, SEntMan.GetComponent<WolfmedIvDripComponent>(dripUid));
            dripPack = SEntMan.SpawnEntity(Pack, map.GridCoords);
            var containers = SEntMan.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainer(dripUid, WolfmedIvDripComponent.ContainerId, out var slot), Is.True);
            Assert.That(containers.Insert(dripPack, slot!), Is.True);
            SEntMan.System<WolfmedIvDripSystem>().Attach(drip, synth);
        });

        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            Assert.That(drip.Comp!.Patient, Is.EqualTo(synth), "the needle came out.");
            Assert.That(Volume(dripPack, "pack"), Is.LessThan(FixedPoint2.New(200)), "the IV gave the synth no hydraulic fluid.");
        });
    }

    [Test]
    public async Task SynthPowerCellSurgeryTest()
    {
        var map = await CreateTestMap();
        EntityUid synth = default, medic = default;

        await Server.WaitAssertion(() =>
        {
            SEntMan.EnsureComponent<Content.Server._Mono.Cleanup.CleanupImmuneComponent>(map.Grid);
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            synth = SEntMan.SpawnEntity("MobSynth", map.GridCoords);
            medic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            var surgery = SEntMan.System<SurgerySystem>();
            var batteries = SEntMan.System<SynthBatterySystem>();
            var hands = SEntMan.System<SharedHandsSystem>();
            var torso = SEntMan.System<SharedBodySystem>().GetBodyChildren(synth)
                .First(part => part.Component.PartType == BodyPartType.Torso).Id;

            Assert.That(batteries.TryGetBattery(synth, out var cell), Is.True, "a fresh synth has no power cell.");
            var cellUid = cell!.Value.Owner;
            Assert.Multiple(() =>
            {
                Assert.That(surgery.WolfmedSurgeryValid(synth, torso, "WFSurgeryExtractSynthPowerCell"), Is.True,
                    "no power cell extraction on a synth's torso.");
                Assert.That(surgery.WolfmedSurgeryValid(synth, torso, "WFSurgeryImplantSynthPowerCell"), Is.False,
                    "a power cell implant is offered on a synth that has one.");
            });

            var multitool = SEntMan.SpawnEntity("Multitool", map.GridCoords);
            RunSteps(surgery, medic, synth, torso, new List<EntityUid> { multitool },
                "WFSurgeryStepLocateSynthPowerCell", "WFSurgeryStepDisconnectSynthPowerCell", "WFSurgeryStepExtractSynthPowerCell");

            Assert.Multiple(() =>
            {
                Assert.That(batteries.TryGetBattery(synth, out _), Is.False, "the power cell is still in the synth.");
                Assert.That(hands.IsHolding(medic, cellUid), Is.True, "the extracted cell is not in the surgeon's hand.");
                Assert.That(surgery.WolfmedSurgeryValid(synth, torso, "WFSurgeryImplantSynthPowerCell"), Is.True,
                    "no power cell implant on an empty synth.");
            });

            RunSteps(surgery, medic, synth, torso, new List<EntityUid> { cellUid, multitool },
                "WFSurgeryStepInsertSynthPowerCell", "WFSurgeryStepConnectSynthPowerCell");

            Assert.That(batteries.TryGetBattery(synth, out var back), Is.True, "the cell did not go back in.");
            Assert.That(back!.Value.Owner, Is.EqualTo(cellUid));
            Assert.That(SEntMan.HasComponent<PowerCellComponent>(cellUid), Is.True);
        });
    }

    [Test]
    public async Task SynthOrganReplacementTest()
    {
        var map = await CreateTestMap();
        EntityUid synth = default;

        await Server.WaitAssertion(() =>
        {
            var protos = Server.ResolveDependency<IPrototypeManager>();
            var fabricator = protos.Index<EntityPrototype>("ExosuitFabricator");
            Assert.That(fabricator.TryGetComponent<LatheComponent>(out var lathe, SEntMan.ComponentFactory), Is.True);
            Assert.That(lathe!.StaticPacks.Select(p => p.Id), Does.Contain("WFSynthOrgansStatic"),
                "the exosuit fabricator does not print synth organs.");
            var results = protos.Index<LatheRecipePackPrototype>("WFSynthOrgansStatic").Recipes
                .Select(r => protos.Index(r).Result?.Id).ToList();
            Assert.That(results, Does.Contain("OrganSynthHeart"), "no replacement synth heart.");

            SEntMan.EnsureComponent<Content.Server._Mono.Cleanup.CleanupImmuneComponent>(map.Grid);
            SEntMan.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            synth = SEntMan.SpawnEntity("MobSynth", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.System<SharedBodySystem>();
            var surgery = SEntMan.System<SurgerySystem>();
            var torso = body.GetBodyChildren(synth).First(part => part.Component.PartType == BodyPartType.Torso).Id;
            var heart = body.GetBodyOrgans(synth).First(organ => SEntMan.HasComponent<HeartComponent>(organ.Id)).Id;

            Assert.That(surgery.WolfmedSurgeryValid(synth, torso, "SurgeryRemoveHeart"), Is.True,
                "a synth's heart cannot be taken out.");
            Assert.That(body.RemoveOrgan(heart), Is.True);
            SEntMan.DeleteEntity(heart);
            Assert.That(surgery.WolfmedSurgeryValid(synth, torso, "SurgeryInsertHeart"), Is.True,
                "a new heart cannot go into a synth.");

            var printed = SEntMan.SpawnEntity("OrganSynthHeart", map.GridCoords);
            Assert.That(body.InsertOrgan(torso, printed, "heart"), Is.True, "a printed synth heart does not fit.");
        });
    }

    private void RunSteps(SurgerySystem surgery, EntityUid user, EntityUid body, EntityUid part, List<EntityUid> tools,
        params string[] steps)
    {
        foreach (var step in steps)
        {
            var singleton = surgery.GetSingleton(step);
            Assert.That(singleton, Is.Not.Null, $"{step} has no singleton.");
            var ev = new SurgeryStepEvent(user, body, part, tools, singleton!.Value);
            SEntMan.EventBus.RaiseLocalEvent(singleton.Value, ref ev);
        }
    }
}
