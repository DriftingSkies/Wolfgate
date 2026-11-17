#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server.Cloning.Components;
using Content.Server.Materials;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server._Mono.Cloning;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server._WG.Wolfmed.Autodoc;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Cloning;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted;
using Content.Shared._WG.Wolfmed.Autodoc;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

/// <summary>
/// The NPC surgeon's M3 and M4: he takes a patient missing an organ from their bed to the autodoc, stocks its tray with
/// the species' own organ and runs it, then puts them back to bed; and he clones a player on the ship's genetic records
/// (his own or the body scanner's) once they die past reviving. His HTN is asleep and his steps are driven by hand, so nobody walks: surgeon, bed, pod
/// and patient share four tiles, each at a tile's centre.
/// </summary>
[TestFixture]
[TestOf(typeof(WGCrewMedicSystem))]
public sealed class WGCrewSurgeonTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  parent: WGLupercalAutodoc
  id: WGTestSurgeonAutodoc
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  parent: GeneticDatabaseServer
  id: WGTestSurgeonDatabase
  components:
  - type: ApcPowerReceiver
    needsPower: false

- type: entity
  parent: WGLupercalCloningPod
  id: WGTestSurgeonCloningPod
  components:
  - type: ApcPowerReceiver
    needsPower: false
";

    /// <summary>He refills his own stock bottles and nothing a player left in the pod, however little of it there is.</summary>
    [Test]
    public async Task TopUpRefillsOnlyHisOwnStock()
    {
        var scene = await Setup(false);
        var server = scene.Pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var slots = entMan.System<ItemSlotsSystem>();
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var uid = entMan.SpawnEntity("WGTestSurgeonAutodoc", scene.Tiles[1]);
            Entity<AutodocComponent> pod = (uid, entMan.GetComponent<AutodocComponent>(uid));

            var bottle = slots.GetItemOrNull(uid, "autodoc_beaker1")!.Value;
            Assert.That(solutions.TryGetDrainableSolution(bottle, out var stock, out var stockContents), Is.True);
            var full = stockContents!.Volume;
            var opiate = stockContents.Contents[0].Reagent;
            solutions.RemoveReagent(stock!.Value, opiate, full / 2);

            var jug = entMan.SpawnEntity("Beaker", scene.Tiles[1]);
            Assert.That(solutions.TryGetDrainableSolution(jug, out var foreign, out var foreignContents), Is.True);
            solutions.TryAddReagent(foreign!.Value, new ReagentId("Omnizine", null), FixedPoint2.New(1), out _);
            Assert.That(slots.TryInsert(uid, "autodoc_beaker3", jug, null), Is.True);

            entMan.System<WGCrewMedicSystem>().TopUp(scene.Surgeon, pod);

            Assert.Multiple(() =>
            {
                Assert.That(stockContents.Volume, Is.EqualTo(stockContents.MaxVolume), "His own bottle is filled again.");
                Assert.That(foreignContents!.Volume, Is.EqualTo(FixedPoint2.New(1)), "A player's beaker is left as it was.");
            });
        });

        await scene.Pair.CleanReturnAsync();
    }

    private sealed record Scene(TestPair Pair, EntityUid Grid, Entity<WGCrewMedicComponent> Surgeon, EntityCoordinates[] Tiles);

    /// <summary>Four floor tiles; the surgeon stands at the centre of the first. Tiles 1 to 3 are one step from him.</summary>
    private static async Task<Scene> Setup(bool connected)
    {
        var pair = await PoolManager.GetServerClient(connected ? new PoolSettings { Connected = true, Dirty = true } : null);
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        Scene scene = default!;
        await server.WaitPost(() =>
        {
            var maps = entMan.System<SharedMapSystem>();
            foreach (var tile in new[] { new Vector2i(1, 0), new Vector2i(0, 1), new Vector2i(1, 1) })
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, tile, map.Tile.Tile);

            var tiles = new[] { new Vector2(0.5f, 0.5f), new Vector2(1.5f, 0.5f), new Vector2(0.5f, 1.5f), new Vector2(1.5f, 1.5f) }
                .Select(v => new EntityCoordinates(map.Grid.Owner, v)).ToArray();
            var surgeon = entMan.System<WGCrewSystem>().SpawnCrewman("WGCrewSurgeon", tiles[0], "surgeontest")!.Value;
            entMan.System<NPCSystem>().SleepNPC(surgeon, entMan.GetComponent<HTNComponent>(surgeon));
            scene = new Scene(pair, map.Grid.Owner, (surgeon, entMan.GetComponent<WGCrewMedicComponent>(surgeon)), tiles);
        });
        return scene;
    }

    /// <summary>
    /// Lets the surgeon work: one look a second until <paramref name="done"/> holds or time runs out. Each second's
    /// <paramref name="trace"/> goes to the test output, so a failure shows what he was doing.
    /// </summary>
    private static async Task Work(Scene scene, Func<bool> done, int seconds, Func<string>? trace = null)
    {
        var server = scene.Pair.Server;
        var medics = server.EntMan.System<WGCrewMedicSystem>();
        for (var i = 0; i < seconds; i++)
        {
            var finished = false;
            await server.WaitPost(() =>
            {
                finished = done();
                if (trace != null)
                    TestContext.Out.WriteLine($"{i}: {trace()}");
                if (!finished)
                    medics.Perform(scene.Surgeon);
            });
            if (finished)
                return;
            await scene.Pair.RunTicksSync(30);
        }
    }

    [Test]
    public async Task MissingOrganIsReplacedInTheAutodoc()
    {
        var scene = await Setup(connected: false);
        var entMan = scene.Pair.Server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        var body = entMan.System<SharedBodySystem>();
        EntityUid patient = default, bed = default;
        Entity<AutodocComponent> pod = new(EntityUid.Invalid, null!);
        await scene.Pair.Server.WaitPost(() =>
        {
            bed = entMan.SpawnEntity("MedicalBed", scene.Tiles[1]);
            var podUid = entMan.SpawnEntity("WGTestSurgeonAutodoc", scene.Tiles[2]);
            pod = (podUid, entMan.GetComponent<AutodocComponent>(podUid));
            patient = entMan.SpawnEntity("MobHuman", scene.Tiles[1]);
            var liver = body.GetBodyOrgans(patient).First(o => entMan.HasComponent<LiverComponent>(o.Id)).Id;
            Assert.That(body.RemoveOrgan(liver), Is.True, "Precondition: the liver comes out.");
            entMan.DeleteEntity(liver);
            Assert.That(entMan.System<SharedBuckleSystem>().TryBuckle(patient, patient, bed, popup: false), Is.True,
                "Precondition: the patient lies in the bed.");

            Assert.That(medics.Missing(pod, patient, out var requirement), Is.Not.Null, "The surgeon sees what is missing.");
            Assert.That(requirement?.Kind, Is.EqualTo(AutodocRequirementKind.Organ));
            Assert.That(medics.NeedsOperation(scene.Surgeon, patient, out var chosen), Is.True, "A missing liver is work for the pod.");
            Assert.That(chosen, Is.EqualTo(pod.Owner));
            medics.Assign(scene.Surgeon, patient);
        });

        var operated = false;
        await Work(scene, () =>
            {
                operated |= scene.Surgeon.Comp.Last == WGCrewMedicAction.Surgery;
                return operated && entMan.GetComponent<BuckleComponent>(patient).BuckledTo == bed;
            }, 150,
            () => $"last {scene.Surgeon.Comp.Last}, patient {scene.Surgeon.Comp.Patient}, pod {scene.Surgeon.Comp.Pod}, "
                  + $"to {scene.Surgeon.Comp.Destination}, ops {scene.Surgeon.Comp.Operations}, state {pod.Comp.State}, "
                  + $"pending {pod.Comp.Pending?.Kind}, queue {pod.Comp.Queue.Count}, mob {entMan.GetComponent<Content.Shared.Mobs.Components.MobStateComponent>(patient).CurrentState}, "
                  + $"buckled {entMan.GetComponent<BuckleComponent>(patient).BuckledTo}, liver {body.GetBodyOrgans(patient).Any(o => entMan.HasComponent<LiverComponent>(o.Id))}");
        await scene.Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(operated, Is.True, "The surgeon ran the autodoc.");
                Assert.That(body.GetBodyOrgans(patient).Any(o => entMan.HasComponent<LiverComponent>(o.Id)), Is.True,
                    "The patient has a liver again.");
                Assert.That(entMan.GetComponent<BuckleComponent>(patient).BuckledTo, Is.EqualTo(bed), "The patient is back in bed.");
                Assert.That(pod.Comp.State, Is.Not.EqualTo(AutodocState.Waiting));
            });
        });

        await scene.Pair.CleanReturnAsync();
    }

    /// <summary>
    /// With no pod aboard he takes the same patient to the operating table, gets the jumpsuit out of the way, puts the
    /// organ back by hand and returns them to bed, awake and with nothing left in his instruments' tray.
    /// </summary>
    [Test]
    public async Task MissingOrganIsReplacedAtTheTable()
    {
        var scene = await Setup(connected: false);
        var entMan = scene.Pair.Server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        var body = entMan.System<SharedBodySystem>();
        EntityUid patient = default, bed = default, table = default;
        await scene.Pair.Server.WaitPost(() =>
        {
            bed = entMan.SpawnEntity("MedicalBed", scene.Tiles[1]);
            table = entMan.SpawnEntity("OperatingTable", scene.Tiles[2]);
            patient = entMan.SpawnEntity("MobHuman", scene.Tiles[1]);
            var suit = entMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", scene.Tiles[1]);
            Assert.That(entMan.System<InventorySystem>().TryEquip(patient, suit, "jumpsuit", silent: true, force: true), Is.True);
            var liver = body.GetBodyOrgans(patient).First(o => entMan.HasComponent<LiverComponent>(o.Id)).Id;
            Assert.That(body.RemoveOrgan(liver), Is.True, "Precondition: the liver comes out.");
            entMan.DeleteEntity(liver);
            Assert.That(entMan.System<SharedBuckleSystem>().TryBuckle(patient, patient, bed, popup: false), Is.True,
                "Precondition: the patient lies in the bed.");

            scene.Surgeon.Comp.TableSpeed = 0.05f;
            Assert.That(medics.NeedsOperation(scene.Surgeon, patient, out _), Is.False, "There is no pod aboard.");
            Assert.That(medics.NeedsTable(scene.Surgeon, patient, out var chosen), Is.True, "A missing liver is work for the table.");
            Assert.That(chosen, Is.EqualTo(table));
            medics.Assign(scene.Surgeon, patient);
        });

        var operated = false;
        await Work(scene, () =>
            {
                operated |= scene.Surgeon.Comp.Last == WGCrewMedicAction.TableSurgery;
                return operated && entMan.GetComponent<BuckleComponent>(patient).BuckledTo == bed;
            }, 200,
            () => $"last {scene.Surgeon.Comp.Last}, patient {scene.Surgeon.Comp.Patient}, table {scene.Surgeon.Comp.Table}, "
                  + $"to {scene.Surgeon.Comp.Destination}, ops {scene.Surgeon.Comp.Operations}, doing {scene.Surgeon.Comp.Procedure?.Surgery}, "
                  + $"step {scene.Surgeon.Comp.Step?.Step}, busy {scene.Surgeon.Comp.Busy}, buckled {entMan.GetComponent<BuckleComponent>(patient).BuckledTo}, "
                  + $"liver {body.GetBodyOrgans(patient).Any(o => entMan.HasComponent<LiverComponent>(o.Id))}");
        await scene.Pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(operated, Is.True, "The surgeon operated at the table.");
                Assert.That(body.GetBodyOrgans(patient).Any(o => entMan.HasComponent<LiverComponent>(o.Id)), Is.True,
                    "The patient has a liver again.");
                Assert.That(entMan.GetComponent<BuckleComponent>(patient).BuckledTo, Is.EqualTo(bed), "The patient is back in bed.");
                Assert.That(entMan.HasComponent<ForcedSleepingComponent>(patient), Is.False, "And is awake.");
                Assert.That(entMan.System<InventorySystem>().TryGetSlotEntity(patient, "jumpsuit", out _), Is.False, "The jumpsuit came off.");
                Assert.That(scene.Surgeon.Comp.Table, Is.Null);
            });

            var kit = scene.Surgeon.Comp.Kit!.Value;
            Assert.That(entMan.System<ItemSlotsSystem>().GetItemOrNull(kit, AutodocComponent.TraySlotId), Is.Null, "Nothing is left in the tray.");
        });

        await scene.Pair.CleanReturnAsync();
    }

    /// <summary>A busy pod sends him to the table; a free one keeps him from it.</summary>
    [Test]
    public async Task TableOnlyWhenNoPodIsFree()
    {
        var scene = await Setup(connected: false);
        var entMan = scene.Pair.Server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        var autodoc = entMan.System<AutodocSystem>();
        var body = entMan.System<SharedBodySystem>();
        await scene.Pair.Server.WaitAssertion(() =>
        {
            var bed = entMan.SpawnEntity("MedicalBed", scene.Tiles[1]);
            var table = entMan.SpawnEntity("OperatingTable", scene.Tiles[3]);
            var podUid = entMan.SpawnEntity("WGTestSurgeonAutodoc", scene.Tiles[2]);
            var pod = new Entity<AutodocComponent>(podUid, entMan.GetComponent<AutodocComponent>(podUid));
            var patient = entMan.SpawnEntity("MobHuman", scene.Tiles[1]);
            var liver = body.GetBodyOrgans(patient).First(o => entMan.HasComponent<LiverComponent>(o.Id)).Id;
            Assert.That(body.RemoveOrgan(liver), Is.True);
            entMan.DeleteEntity(liver);
            Assert.That(entMan.System<SharedBuckleSystem>().TryBuckle(patient, patient, bed, popup: false), Is.True);

            Assert.That(medics.NeedsOperation(scene.Surgeon, patient, out var chosenPod), Is.True, "The pod is free.");
            Assert.That(chosenPod, Is.EqualTo(podUid));
            Assert.That(medics.NeedsTable(scene.Surgeon, patient, out _), Is.False, "So the table isn't for this one.");

            var other = entMan.SpawnEntity("MobHuman", scene.Tiles[2]);
            Assert.That(autodoc.TryInsert(pod, other), Is.True, "Precondition: someone else is in the pod.");
            Assert.That(medics.NeedsOperation(scene.Surgeon, patient, out _), Is.False, "The pod is busy.");
            Assert.That(medics.NeedsTable(scene.Surgeon, patient, out var chosenTable), Is.True, "So he takes the table.");
            Assert.That(chosenTable, Is.EqualTo(table));

            entMan.DeleteEntity(other);
        });

        await scene.Pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlayerOnFileIsCloned()
    {
        var scene = await Setup(connected: true);
        var server = scene.Pair.Server;
        var entMan = server.EntMan;
        var medics = entMan.System<WGCrewMedicSystem>();
        EntityUid dbUid = default, podUid = default;
        await server.WaitPost(() =>
        {
            dbUid = entMan.SpawnEntity("WGTestSurgeonDatabase", scene.Tiles[1]);
            podUid = entMan.SpawnEntity("WGTestSurgeonCloningPod", scene.Tiles[2]);
        });

        // The machines' power receivers settle on the next ticks.
        await scene.Pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            var mindSys = entMan.System<SharedMindSystem>();
            var db = new Entity<GeneticDatabaseComponent>(dbUid, entMan.GetComponent<GeneticDatabaseComponent>(dbUid));
            var pod = entMan.GetComponent<CloningPodComponent>(podUid);
            entMan.System<MaterialStorageSystem>().TryChangeMaterialAmount(podUid, pod.RequiredMaterial, 10000);

            var session = server.PlayerMan.Sessions.First();
            var patient = entMan.SpawnEntity("MobHuman", scene.Tiles[3]);
            var mind = mindSys.CreateMind(session.UserId, "Tester");
            mindSys.TransferTo(mind, patient, mind: mind.Comp);
            mindSys.SetUserId(mind, session.UserId, mind.Comp);

            Assert.That(medics.TryRegister(scene.Surgeon, patient, out var reason), Is.True, reason);
            Assert.That(medics.OnFile(db, mind), Is.True, "The surgeon put the player on file.");

            medics.Withdraw(scene.Surgeon, patient);
            Assert.That(medics.OnFile(db, mind), Is.False, "Taken off file at their asking, record and all.");

            // The v0.9 review: a record made at the body scanner is his to clone from too.
            var scanned = entMan.System<GeneticCloningSystem>().CreateRecord(patient, out var fail);
            Assert.That(scanned, Is.Not.Null, fail);
            entMan.System<GeneticDatabaseSystem>().Upsert(db, scanned!);

            entMan.EnsureComponent<UnrevivableComponent>(patient);
            entMan.System<MobStateSystem>().ChangeMobState(patient, MobState.Dead);
            var crew = entMan.GetComponent<WGCrewComponent>(scene.Surgeon);
            scene.Surgeon.Comp.NextCloneCheck = TimeSpan.Zero;
            medics.TickCloning(scene.Surgeon, crew);

            var clone = pod.BodyContainer.ContainedEntity;
            Assert.Multiple(() =>
            {
                Assert.That(clone, Is.Not.Null, "The surgeon started the cloner.");
                Assert.That(scene.Surgeon.Comp.Last, Is.EqualTo(WGCrewMedicAction.Clone));
                Assert.That(entMan.HasComponent<ActiveCloningPodComponent>(podUid), Is.True);
            });
            Assert.That(entMan.GetComponent<BeingClonedComponent>(clone!.Value).Mind, Is.EqualTo(mind.Comp),
                "The clone waits for the scanned player's mind.");

            entMan.DeleteEntity(clone.Value);
            entMan.DeleteEntity(patient);
            entMan.DeleteEntity(mind);
        });

        await scene.Pair.CleanReturnAsync();
    }
}
