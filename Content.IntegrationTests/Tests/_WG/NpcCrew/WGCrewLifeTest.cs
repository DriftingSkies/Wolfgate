#nullable enable
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.AdminVessels;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipPa;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.FixedPoint;
using Content.Shared._Onyx.Wounds;
using Content.Shared.Power;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

/// <summary>
/// The crew-life pass: vermin are no boarders and borers always are, janitors hunt mice, general quarters sends crew
/// to their stations, medics use real medipens and let a patient walk off, holodoors close behind you and the core
/// gates cycle. HTNs sleep and steps are driven by hand, so nobody walks.
/// </summary>
[TestFixture]
public sealed partial class WGCrewLifeTest
{
    private sealed record Scene(TestPair Pair, EntityUid Grid, EntityCoordinates Origin);

    private static async Task<Scene> Setup()
    {
        var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitPost(() =>
        {
            var maps = server.EntMan.System<SharedMapSystem>();
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
        });
        return new Scene(pair, map.Grid.Owner, map.GridCoords);
    }

    private static EntityUid Crewman(IEntityManager entMan, Scene scene, string role, float x = 0, float y = 0)
    {
        var uid = entMan.System<WGCrewSystem>().SpawnCrewman(role, scene.Origin.Offset(new System.Numerics.Vector2(x, y)), "lifetest")!.Value;
        entMan.System<NPCSystem>().SleepNPC(uid, entMan.GetComponent<HTNComponent>(uid));
        return uid;
    }

    [Test]
    public async Task VerminAreNoBoardersAndBorersAlwaysAre()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        await scene.Pair.Server.WaitAssertion(() =>
        {
            var security = entMan.System<WGCrewSecuritySystem>();
            var deckhand = Crewman(entMan, scene, "WGCrewDeckhand");
            var mouse = entMan.SpawnEntity("MobMouse", scene.Origin.Offset(new System.Numerics.Vector2(2, 0)));
            var borer = entMan.SpawnEntity("MobCorticalBorer", scene.Origin.Offset(new System.Numerics.Vector2(-2, 0)));
            var human = entMan.SpawnEntity("MobHuman", scene.Origin.Offset(new System.Numerics.Vector2(0, 2)));

            Assert.That(security.IsFauna(deckhand, mouse), Is.True, "A mouse is fauna, not a boarder.");
            Assert.That(security.IsFauna(deckhand, human), Is.False, "A person is never fauna.");
            Assert.That(security.IsFauna(deckhand, borer), Is.False, "A borer is never fauna.");
            Assert.That(security.IsAuthorized(deckhand, borer), Is.False, "A borer is never welcome aboard.");
            Assert.That(security.BoardingResponse(deckhand, borer, entMan.EnsureComponent<WGCrewSecurityComponent>(deckhand)),
                Is.EqualTo(WGCrewSecurityResponse.Hostile), "A borer is answered as hostile whatever the rule.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task JanitorsHuntMice()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        EntityUid janitor = default;
        EntityUid mouse = default;
        await scene.Pair.Server.WaitPost(() =>
        {
            janitor = Crewman(entMan, scene, "WGCrewJanitor");
            mouse = entMan.SpawnEntity("MobMouse", scene.Origin.Offset(new System.Numerics.Vector2(2, 0)));
            // The test map has no air and is small: both stay alive and the mouse stays put while the hunt is looked at.
            entMan.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(mouse);
            entMan.System<Content.Shared.Damage.Systems.SharedGodmodeSystem>().EnableGodmode(janitor);
            if (entMan.TryGetComponent<HTNComponent>(mouse, out var mouseBrain))
                entMan.System<NPCSystem>().SleepNPC(mouse, mouseBrain);
            var hunter = entMan.GetComponent<WGCrewPestHunterComponent>(janitor);
            hunter.Chance = 1f;
            hunter.NextLook = System.TimeSpan.Zero;
        });

        await scene.Pair.RunTicksSync(90);
        await scene.Pair.Server.WaitAssertion(() =>
        {
            var hunter = entMan.GetComponent<WGCrewPestHunterComponent>(janitor);
            Assert.That(hunter.Target, Is.EqualTo(mouse), "The janitor goes after the mouse.");
            var mobs = entMan.System<Content.Shared.Mobs.Systems.MobStateSystem>();
            var now = scene.Pair.Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().CurTime;
            var why = $"hunt ends {hunter.Until} at {now}, mouse alive {mobs.IsAlive(mouse)}, "
                      + $"janitor alive {mobs.IsAlive(janitor)}, mouse grid {entMan.GetComponent<TransformComponent>(mouse).GridUid}, "
                      + $"janitor grid {entMan.GetComponent<TransformComponent>(janitor).GridUid}";
            Assert.That(entMan.System<WGCrewPestSystem>().TryGetPest(janitor, out var pest, out _), Is.True, why);
            Assert.That(pest, Is.EqualTo(mouse));
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task GeneralQuartersSendsCrewToStations()
    {
        var scene = await Setup();
        var entMan = scene.Pair.Server.EntMan;
        await scene.Pair.Server.WaitAssertion(() =>
        {
            var readiness = entMan.System<WGCrewReadinessSystem>();
            var deckhand = Crewman(entMan, scene, "WGCrewDeckhand");
            var janitor = Crewman(entMan, scene, "WGCrewJanitor", 1);
            var gunner = Crewman(entMan, scene, "WGCrewGunner", -1);
            var alert = entMan.EnsureComponent<ShipAlertComponent>(scene.Grid);

            Assert.That(readiness.AtStations(deckhand), Is.False, "Precondition: Code Green.");
            alert.GeneralQuarters = true;
            Assert.That(readiness.Readiness(scene.Grid), Is.EqualTo(WGCrewReadiness.Battle));
            Assert.That(readiness.AtStations(deckhand), Is.True, "A deckhand goes to his station.");
            Assert.That(readiness.StandsToArms(deckhand), Is.True, "An on-sight hand stands to with his weapon.");
            Assert.That(readiness.AtStations(janitor), Is.True, "The janitor drops his mop for his post.");
            Assert.That(readiness.StandsToArms(janitor), Is.False, "He doesn't draw on anyone.");
            Assert.That(readiness.AtStations(gunner), Is.False, "The guns keep their own duty.");

            alert.GeneralQuarters = false;
            alert.Code = "WGShipCodeYellow";
            Assert.That(readiness.Readiness(scene.Grid), Is.EqualTo(WGCrewReadiness.Alert));
            Assert.That(readiness.AtStations(deckhand), Is.False, "Yellow is readiness, not stations.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task MedipensGoInAndLandOnTheFloor()
    {
        var scene = await Setup();
        var server = scene.Pair.Server;
        var entMan = server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        Entity<WGCrewMedicComponent> medic = default;
        WGCrewMedicComponent medicComp = default!;
        EntityUid patient = default;
        await server.WaitPost(() =>
        {
            var uid = Crewman(entMan, scene, "WGCrewParamedic");
            medicComp = entMan.GetComponent<WGCrewMedicComponent>(uid);
            medic = (uid, medicComp);
            patient = entMan.SpawnEntity("MobHuman", scene.Origin.Offset(new System.Numerics.Vector2(1, 0)));
            var arm = entMan.System<SharedBodySystem>().GetBodyChildren(patient)
                .First(p => p.Component.PartType == BodyPartType.Arm).Id;
            var blunt = new DamageSpecifier { DamageDict = { [new ProtoId<DamageTypePrototype>("Blunt")] = FixedPoint2.New(50) } };
            Assert.That(entMan.System<WoundDamageRoutingSystem>().TryApplyPartDamage(patient, arm, blunt), Is.True);
            medics.Assign(medic, patient);
        });

        for (var i = 0; i < 60 && medicComp.Last != WGCrewMedicAction.BrutePen; i++)
        {
            await server.WaitPost(() => medics.Perform(medic));
            await scene.Pair.RunTicksSync(30);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(medicComp.Last, Is.EqualTo(WGCrewMedicAction.BrutePen), "Heavy brute gets a brute auto-injector.");
            var containers = entMan.System<SharedContainerSystem>();
            var pens = new System.Collections.Generic.List<EntityUid>();
            var query = entMan.EntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out var uid, out var meta))
            {
                if (meta.EntityPrototype?.ID == "BruteAutoInjector")
                    pens.Add(uid);
            }
            Assert.That(pens, Is.Not.Empty, "The pen was a real one.");
            Assert.That(pens.Any(pen => !containers.IsEntityInContainer(pen)
                && entMan.GetComponent<TransformComponent>(pen).GridUid == scene.Grid), Is.True, "The spent pen lies on the floor.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task APatientWhoWalksOffIsLetGo()
    {
        var scene = await Setup();
        var server = scene.Pair.Server;
        var entMan = server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        var buckle = entMan.System<SharedBuckleSystem>();
        EntityUid uid = default;
        EntityUid patient = default;
        await server.WaitAssertion(() =>
        {
            uid = Crewman(entMan, scene, "WGCrewParamedic");
            var bedAt = scene.Origin.Offset(new System.Numerics.Vector2(1, 1));
            var bed = entMan.SpawnEntity("MedicalBed", bedAt);
            patient = entMan.SpawnEntity("MobHuman", bedAt);
            Assert.That(buckle.TryBuckle(patient, patient, bed, popup: false), Is.True, "Precondition: the patient lies down.");
        });

        // Past the buckle's unbuckle delay. He is given the patient only now, so his own sweep can't let them go first.
        await scene.Pair.RunTicksSync(60);
        await server.WaitAssertion(() =>
        {
            var medic = new Entity<WGCrewMedicComponent>(uid, entMan.GetComponent<WGCrewMedicComponent>(uid));
            medics.Assign(medic, patient);
            Assert.That(medic.Comp.Patient, Is.EqualTo(patient), "Precondition: he has the patient when they get up.");
            Assert.That(buckle.TryUnbuckle(patient, patient, popup: false), Is.True, "Precondition: the patient gets up.");
            entMan.System<SharedTransformSystem>().SetCoordinates(patient, scene.Origin.Offset(new System.Numerics.Vector2(-2, -2)));
            Assert.That(medics.NeedsTakingIn(medic, patient), Is.False, "Someone on their feet isn't hauled back to bed.");
            medics.Perform(medic);
            Assert.That(medic.Comp.Patient, Is.Null, "He lets them go.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task HolodoorsCloseBehindYou()
    {
        var scene = await Setup();
        var server = scene.Pair.Server;
        var entMan = server.EntMan;
        EntityUid door = default;
        await server.WaitPost(() =>
        {
            door = entMan.SpawnEntity("WGHoloDoor", scene.Origin.Offset(new System.Numerics.Vector2(2, 2)));
            entMan.System<SharedDoorSystem>().StartOpening(door);
        });

        await scene.Pair.RunTicksSync(30);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Open), "Precondition: the field parted."));

        await scene.Pair.RunTicksSync(300);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Closed), "The field closed by itself."));

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task InterlockedDoorsCycle()
    {
        var scene = await Setup();
        var server = scene.Pair.Server;
        var entMan = server.EntMan;
        var doors = entMan.System<SharedDoorSystem>();
        EntityUid outer = default;
        EntityUid inner = default;
        EntityUid walker = default;
        // Holodoors: plain doors with no airlock timers or power of their own to get in the way.
        await server.WaitPost(() =>
        {
            outer = entMan.SpawnEntity("WGHoloDoor", scene.Origin.Offset(new System.Numerics.Vector2(0, 2)));
            inner = entMan.SpawnEntity("WGHoloDoor", scene.Origin.Offset(new System.Numerics.Vector2(0, -2)));
            walker = entMan.SpawnEntity("MobHuman", scene.Origin.Offset(new System.Numerics.Vector2(1, -1)));
            entMan.EnsureComponent<WGDoorInterlockComponent>(outer).Partner = inner;
            entMan.EnsureComponent<WGDoorInterlockComponent>(inner).Partner = outer;
            doors.StartOpening(outer);
        });

        await scene.Pair.RunTicksSync(30);
        await server.WaitPost(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(outer).State, Is.EqualTo(DoorState.Open), "Precondition: the outer door is open.");
            Assert.That(doors.TryOpen(inner, user: walker), Is.False, "The inner door won't open while the outer stands open.");
        });

        await scene.Pair.RunTicksSync(45);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorComponent>(outer).State, Is.EqualTo(DoorState.Closed), "Asking at the inner door closed the outer.");
            Assert.That(entMan.GetComponent<DoorComponent>(inner).State, Is.AnyOf(DoorState.Opening, DoorState.Open),
                "Then the inner door opened.");
        });

        await scene.Pair.CleanReturnAsync();
    }
}
