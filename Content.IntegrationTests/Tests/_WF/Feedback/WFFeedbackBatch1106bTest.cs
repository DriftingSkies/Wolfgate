#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Drydock;
using Content.IntegrationTests.Tests._WF.Planets;
using Content.Server._WF.Drydock;
using Content.Server._WF.PlanetComms;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Lathe;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._White.Standing;
using Content.Shared.Body.Prototypes;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Gravity;
using Content.Shared.Research.Prototypes;
using Content.Shared.Standing;
using Content.Server.Standing;
using Content.Shared._WF.Drydock;
using Robust.Shared.Containers;
using Robust.Shared.Maths;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Player reports from the late 2026-11-06 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1106bTest
{
    /// <summary>Report 568: the forge smelts the ore it is given into ingots.</summary>
    [Test]
    public async Task ForgeSmeltsOre()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var lathe = server.System<LatheSystem>();
        var materials = server.System<MaterialStorageSystem>();
        var map = await pair.CreateTestMap();
        EntityUid forge = default;

        await server.WaitPost(() =>
        {
            forge = em.SpawnEntity("Forge", map.GridCoords);
            Assert.That(materials.TryChangeMaterialAmount(forge, "RawSilver", 100), Is.True, "The forge took no ore.");
            var recipe = server.ProtoMan.Index<LatheRecipePrototype>("IngotSilver1");
            Assert.That(lathe.TryAddToQueue(forge, recipe, 1), Is.True, "The forge refused a silver ingot.");
            Assert.That(lathe.TryStartProducing(forge), Is.True, "The forge didn't start smelting.");
        });
        await server.WaitRunTicks(30);

        // Smelting turns the ore into stock the forge holds, ejected from its menu as the ore processor's is.
        await server.WaitAssertion(() =>
        {
            Assert.That(materials.GetMaterialAmount(forge, "RawSilver"), Is.EqualTo(0), "The forge kept the ore.");
            Assert.That(materials.GetMaterialAmount(forge, "Silver"), Is.EqualTo(100), "The forge produced no silver.");
            Assert.That(em.GetComponent<MaterialStorageComponent>(forge).CanEjectStoredMaterials, Is.True, "The silver can't be taken out.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 556: better capacitors alone cut a mobile drydock crane's working draw and nothing else.</summary>
    [Test]
    public async Task CapacitorsAloneCutCraneDraw()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var (_, crane, _) = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            var comp = em.GetComponent<WFDrydockCraneComponent>(crane);
            var stock = comp.WorkingLoad;

            SetParts(em, crane, new Dictionary<string, string> { ["Capacitor"] = "AdvancedCapacitorStockPart" });
            Assert.Multiple(() =>
            {
                Assert.That(comp.WorkingLoad, Is.EqualTo(stock * 0.9f).Within(1f), "Mk II capacitors should trim the draw by a tenth.");
                Assert.That(comp.SpeedMultiplier, Is.EqualTo(1f));
                Assert.That(comp.MaxArm, Is.EqualTo(29f));
            });

            SetParts(em, crane, new Dictionary<string, string> { ["Capacitor"] = "QuadraticCapacitorStockPart" });
            Assert.That(comp.WorkingLoad, Is.LessThan(stock * 0.9f), "Better capacitors should trim the draw further.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 567: a crewmate can lie down for surgery on a deck with no gravity.</summary>
    [Test]
    public async Task CanLieDownWithoutGravity()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var laying = server.System<LayingDownSystem>();
        var standing = server.System<StandingStateSystem>();
        var gravity = server.System<SharedGravitySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            em.RemoveComponent<GravityComponent>(map.Grid);
            var mob = em.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(gravity.EntityOnGravitySupportingGridOrMap(mob), Is.False, "The test deck should be weightless.");
            Assert.That(laying.TryLieDown(mob), Is.True, "Could not lie down on a weightless deck.");
            Assert.That(standing.IsDown(mob), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 551: a world's transceiver the site stash has paused keeps its comms up.</summary>
    [Test]
    public async Task PausedTransceiverKeepsWorldUp()
    {
        await using var pair = await PoolManager.GetServerClient();
        await PlanetFixture.EnableFeature(pair);
        var server = pair.Server;
        var em = server.EntMan;
        var comms = server.System<WFPlanetCommsSystem>();
        var power = server.System<PowerReceiverSystem>();
        var meta = server.System<MetaDataSystem>();

        var layers = await PlanetFixture.BuildStandalone(pair);
        var ground = layers[0];
        await PlanetFixture.LayTiles(pair, ground, new Vector2i(-2, -2), new Vector2i(4, 4));
        EntityUid transceiver = default;

        await server.WaitPost(() =>
        {
            transceiver = em.SpawnEntity("WFMassTransceiver", new EntityCoordinates(ground, new Vector2(0.5f, 0.5f)));
            power.SetNeedsPower(transceiver, false);
        });
        await server.WaitRunTicks(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<ApcPowerReceiverComponent>(transceiver).Powered, Is.True);
            Assert.That(comms.IsCommsUp(ground), Is.True, "A powered transceiver left the world dark.");
            meta.SetEntityPaused(transceiver, true);
        });
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<MetaDataComponent>(transceiver).EntityPaused, Is.True);
            Assert.That(comms.IsCommsUp(ground), Is.True, "Pausing the transceiver took the world's comms down.");
            meta.SetEntityPaused(transceiver, false);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 531: kidneys can be taken out and put back, a shadekin's included.</summary>
    [Test]
    public async Task KidneysCanBeSwapped()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var factory = server.ResolveDependency<IComponentFactory>();
            var protos = server.ProtoMan;
            Assert.Multiple(() =>
            {
                Assert.That(protos.HasIndex<EntityPrototype>("WFSurgeryRemoveKidneys"), Is.True);
                Assert.That(protos.HasIndex<EntityPrototype>("WFSurgeryInsertKidneys"), Is.True);
                foreach (var body in new[] { "Human", "Shadekin" })
                {
                    var organ = protos.Index<BodyPrototype>(body).Slots["torso"].Organs["kidneys"];
                    Assert.That(protos.Index<EntityPrototype>(organ).TryGetComponent<WFKidneysComponent>(out _, factory), Is.True,
                        $"{organ} is not marked as kidneys, so no surgery finds it.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 566: a board lists its machine parts under requirements; a part under stackRequirements can never be fitted.</summary>
    [Test]
    public async Task MachinePartsSitUnderRequirements()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var factory = server.ResolveDependency<IComponentFactory>();
            var parts = server.ProtoMan.EnumeratePrototypes<MachinePartPrototype>().Select(p => p.ID).ToHashSet();
            var wrong = new List<string>();
            foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryGetComponent<MachineBoardComponent>(out var board, factory))
                    continue;

                wrong.AddRange(board.StackRequirements.Keys.Where(stack => parts.Contains(stack.Id)).Select(stack => $"{proto.ID}: {stack.Id}"));
            }

            Assert.That(wrong, Is.Empty, "machine parts listed as stacks");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Swaps a machine's parts of the given types for the given prototypes, then refreshes its upgrades.</summary>
    private static void SetParts(IEntityManager em, EntityUid uid, Dictionary<string, string> parts)
    {
        var machine = em.GetComponent<MachineComponent>(uid);
        var containers = em.System<SharedContainerSystem>();
        var coords = em.GetComponent<TransformComponent>(uid).Coordinates;
        foreach (var part in machine.PartContainer.ContainedEntities.ToList())
        {
            if (!em.TryGetComponent<MachinePartComponent>(part, out var comp) || !parts.TryGetValue(comp.PartType.Id, out var replacement))
                continue;

            containers.Remove(part, machine.PartContainer);
            em.DeleteEntity(part);
            containers.Insert(em.SpawnEntity(replacement, coords), machine.PartContainer);
        }

        em.System<ConstructionSystem>().RefreshParts(uid, machine);
    }
}
