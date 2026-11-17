#nullable enable
using System;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted;
using Content.Shared._Onyx.Wounds;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

/// <summary>
/// NPC medics: a paramedic dresses a bleeding limb where the patient lies, gives the unconscious a stimulant, takes a
/// badly hurt patient to a free bed in Medical, and finds patients lying in a bed by himself. The medic's HTN is put to
/// sleep and his steps are driven by hand, so nobody walks: medic, patient and bed share a few adjacent tiles.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCrewMedicSystem))]
public sealed class WGCrewMedicTest
{
    private sealed record Scene(TestPair Pair, EntityUid Grid, Entity<WGCrewMedicComponent> Medic, EntityUid Patient, EntityCoordinates Corner);

    private static async Task<Scene> Setup(string role = "WGCrewParamedic")
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        Scene scene = default!;
        await server.WaitPost(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            foreach (var tile in new[] { new Vector2i(1, 0), new Vector2i(0, 1), new Vector2i(1, 1) })
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, tile, map.Tile.Tile);

            var medic = entMan.System<WGCrewSystem>().SpawnCrewman(role, map.GridCoords, "medictest")!.Value;
            entMan.System<NPCSystem>().SleepNPC(medic, entMan.GetComponent<HTNComponent>(medic));
            var patient = entMan.SpawnEntity("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(1, 0)));
            scene = new Scene(pair, map.Grid.Owner, (medic, entMan.GetComponent<WGCrewMedicComponent>(medic)), patient,
                map.GridCoords.Offset(new System.Numerics.Vector2(1, 1)));
        });
        return scene;
    }

    /// <summary>Lets the medic work: one look a second until <paramref name="done"/> holds or time runs out.</summary>
    private static async Task Work(Scene scene, Func<bool> done, int seconds = 60)
    {
        var server = scene.Pair.Server;
        var medics = server.EntMan.System<WGCrewMedicSystem>();
        for (var i = 0; i < seconds; i++)
        {
            var finished = false;
            await server.WaitPost(() =>
            {
                finished = done();
                if (!finished)
                    medics.Perform(scene.Medic);
            });
            if (finished)
                return;
            await scene.Pair.RunTicksSync(30);
        }
    }

    private static EntityUid Part(IEntityManager entMan, EntityUid body, BodyPartType type, BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entMan.System<SharedBodySystem>().GetBodyChildren(body)
            .First(p => p.Component.PartType == type && (symmetry == BodyPartSymmetry.None || p.Component.Symmetry == symmetry)).Id;
    }

    private static DamageSpecifier Blunt(int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(amount) },
    };

    [Test]
    public async Task BleedingIsDressedWhereTheyLie()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        var bleeding = entMan.System<WoundBleedingSystem>();
        var medics = entMan.System<WGCrewMedicSystem>();
        EntityUid arm = default;
        var before = 0f;
        await scene.Pair.Server.WaitPost(() =>
        {
            arm = Part(entMan, scene.Patient, BodyPartType.Arm, BodyPartSymmetry.Left);
            Assert.That(entMan.System<WoundSystem>().CreateOrMergeWound(arm, "SlashWound", 20), Is.Not.Null);
            before = bleeding.GetPartRate(arm);
            Assert.That(before, Is.GreaterThan(0f), "Precondition: the arm bleeds.");
            medics.Assign(scene.Medic, scene.Patient);
        });

        await Work(scene, () => scene.Medic.Comp.Last is WGCrewMedicAction.Dress or WGCrewMedicAction.Tourniquet);
        await scene.Pair.Server.WaitAssertion(() =>
        {
            Assert.That(scene.Medic.Comp.Last, Is.AnyOf(WGCrewMedicAction.Dress, WGCrewMedicAction.Tourniquet), "The medic saw to the bleed.");
            Assert.That(bleeding.GetPartRate(arm) < before || bleeding.IsTiedOff(arm), Is.True, "The arm bleeds less, or is tied off.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnconsciousGetAStimulant()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        await scene.Pair.Server.WaitPost(() =>
        {
            entMan.System<MobStateSystem>().ChangeMobState(scene.Patient, MobState.Critical);
            Assert.That(medics.NextAction(scene.Medic, scene.Patient, out _), Is.EqualTo(WGCrewMedicAction.Stimulant),
                "An unconscious patient with nothing bleeding gets a stimulant first.");
            medics.Assign(scene.Medic, scene.Patient);
            Assert.That(medics.Perform(scene.Medic), Is.False, "The treatment is under way.");
            Assert.That(scene.Medic.Comp.Busy, Is.True);
        });

        await scene.Pair.RunTicksSync(120);
        await scene.Pair.Server.WaitAssertion(() =>
        {
            Assert.That(scene.Medic.Comp.Last, Is.EqualTo(WGCrewMedicAction.Stimulant));
            Assert.That(medics.CareOf(scene.Patient)?.NextStimulant, Is.GreaterThan(TimeSpan.Zero), "The next dose waits a minute.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// The audit's loop: a patient was let go and taken again at once, each claim restarting the stimulant's clock, and a
    /// body with nowhere to go was taken every second. Now the dose's clock is the patient's, and the body is let be.
    /// </summary>
    [Test]
    public async Task ReclaimingKeepsTheDoseClockAndNoBedMeansNoLoop()
    {
        var scene = await Setup();
        var server = scene.Pair.Server;
        var entMan = server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        var mobs = entMan.System<MobStateSystem>();
        // A downed patient with no wounds to dress, so the stimulant is all there is to give where they lie.
        EntityUid patient = default;
        await server.WaitPost(() =>
        {
            patient = entMan.SpawnEntity("MobHuman", entMan.GetComponent<TransformComponent>(scene.Patient).Coordinates);
            mobs.ChangeMobState(patient, MobState.Critical);
            Assert.That(medics.NeedsTakingIn(scene.Medic, patient), Is.True, "Precondition: a downed patient goes to Medical.");
            medics.Assign(scene.Medic, patient);
            Assert.That(medics.Perform(scene.Medic), Is.False, "With no bed aboard he gives the stimulant where they lie.");
        });

        await scene.Pair.RunTicksSync(120);
        await server.WaitAssertion(() =>
        {
            Assert.That(scene.Medic.Comp.Last, Is.EqualTo(WGCrewMedicAction.Stimulant), "Precondition: the dose went in.");
            mobs.ChangeMobState(patient, MobState.Critical);
            medics.Release(scene.Medic);
            medics.Assign(scene.Medic, patient);
            Assert.That(medics.NextAction(scene.Medic, patient, out _), Is.Not.EqualTo(WGCrewMedicAction.Stimulant),
                "Taking the patient again doesn't restart the dose's clock.");
        });

        // The loop the audit found was a body with nowhere to go, let go and taken again every second: now it's let be.
        // (A downed patient keeps him busy dressing the wounds crit opens, as it should, so a body shows the loop.)
        EntityUid body = default;
        await server.WaitPost(() =>
        {
            medics.Release(scene.Medic);
            body = entMan.SpawnEntity("MobHuman", scene.Corner);
            entMan.EnsureComponent<UnrevivableComponent>(body);
            mobs.ChangeMobState(body, MobState.Dead);
            medics.Assign(scene.Medic, body);
            Assert.That(medics.Perform(scene.Medic), Is.True, "No drawer aboard: nothing to do for the body.");
            Assert.That(scene.Medic.Comp.Patient, Is.Null, "He lets the body be.");
        });

        for (var second = 0; second < 3; second++)
        {
            await scene.Pair.RunTicksSync(30);
            await server.WaitAssertion(() =>
                Assert.That(scene.Medic.Comp.Patient, Is.Not.EqualTo(body), "He doesn't take the body again while no drawer is free."));
        }

        await scene.Pair.CleanReturnAsync();
    }

    /// <summary>A body with no free drawer to go to no longer holds the medic: a living patient comes first.</summary>
    [Test]
    public async Task TheLivingComeBeforeABodyWithNowhereToGo()
    {
        var scene = await Setup();
        var server = scene.Pair.Server;
        var entMan = server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        var mobs = entMan.System<MobStateSystem>();
        await server.WaitPost(() =>
        {
            var body = entMan.SpawnEntity("MobHuman", scene.Corner);
            entMan.EnsureComponent<UnrevivableComponent>(body);
            mobs.ChangeMobState(body, MobState.Dead);
            Assert.That(medics.NeedsTakingIn(scene.Medic, body), Is.True, "Precondition: the body is for the morgue.");
            medics.Assign(scene.Medic, body);
            Assert.That(medics.Perform(scene.Medic), Is.True, "No drawer aboard: nothing to do for the body.");
            Assert.That(scene.Medic.Comp.Patient, Is.Null, "He lets the body be.");
            // Downed by real damage: a state set by hand goes back to Alive at the mob's next check.
            var torso = Part(entMan, scene.Patient, BodyPartType.Torso);
            Assert.That(entMan.System<WoundDamageRoutingSystem>().TryApplyPartDamage(scene.Patient, torso, Blunt(110)), Is.True);
        });

        await scene.Pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(scene.Medic.Comp.Patient, Is.EqualTo(scene.Patient),
                $"The living patient comes before the body (next {medics.NextAction(scene.Medic, scene.Patient, out _)}, "
                + $"takes in {medics.NeedsTakingIn(scene.Medic, scene.Patient)}, no room until {medics.CareOf(scene.Patient)?.NoRoomUntil}, "
                + $"state {entMan.GetComponent<MobStateComponent>(scene.Patient).CurrentState}).");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task BadlyHurtAreTakenToABed()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        EntityUid bed = default;
        await scene.Pair.Server.WaitPost(() =>
        {
            bed = entMan.SpawnEntity("MedicalBed", scene.Corner);
            var torso = Part(entMan, scene.Patient, BodyPartType.Torso);
            Assert.That(entMan.System<WoundDamageRoutingSystem>().TryApplyPartDamage(scene.Patient, torso, Blunt(140)), Is.True);
            Assert.That(medics.NeedsTakingIn(scene.Medic, scene.Patient), Is.True, "Precondition: too hurt to patch up where they lie.");
            medics.Assign(scene.Medic, scene.Patient);
        });

        await Work(scene, () => entMan.GetComponent<BuckleComponent>(scene.Patient).BuckledTo == bed, 90);
        await scene.Pair.Server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BuckleComponent>(scene.Patient).BuckledTo, Is.EqualTo(bed), "The patient is in the bed.");
            Assert.That(medics.OnBed(scene.Patient), Is.True);
            Assert.That(medics.NeedsTakingIn(scene.Medic, scene.Patient), Is.False, "A patient in a bed is in Medical.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task PatientsInBedsAreFoundAndPatched()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        await scene.Pair.Server.WaitPost(() =>
        {
            var bed = entMan.SpawnEntity("MedicalBed", scene.Corner);
            var arm = Part(entMan, scene.Patient, BodyPartType.Arm, BodyPartSymmetry.Right);
            Assert.That(entMan.System<WoundDamageRoutingSystem>().TryApplyPartDamage(scene.Patient, arm, Blunt(15)), Is.True);
            entMan.System<SharedTransformSystem>().SetCoordinates(scene.Patient, scene.Corner);
            Assert.That(entMan.System<Content.Shared.Buckle.SharedBuckleSystem>().TryBuckle(scene.Patient, scene.Patient, bed, popup: false), Is.True,
                "Precondition: the patient lies down in the bed.");
        });

        await scene.Pair.RunTicksSync(60);
        await scene.Pair.Server.WaitAssertion(() =>
        {
            Assert.That(scene.Medic.Comp.Patient, Is.EqualTo(scene.Patient), "The medic found the patient in the bed by himself.");
        });

        await Work(scene, () => scene.Medic.Comp.Last == WGCrewMedicAction.Brute);
        await scene.Pair.Server.WaitAssertion(() =>
        {
            Assert.That(scene.Medic.Comp.Last, Is.EqualTo(WGCrewMedicAction.Brute), "Minor brute is patched in the bed.");
        });

        await scene.Pair.CleanReturnAsync();
    }
}
