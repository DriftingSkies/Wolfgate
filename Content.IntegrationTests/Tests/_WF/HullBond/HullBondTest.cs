#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.ShipRepair;
using Content.Server._WF.HullBond;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Shared.Construction.Components;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Server.Physics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.HullBond;

/// <summary>
/// Ship fittings bond to the hull they are first bolted to: they move around it freely, refuse any other hull, and
/// can't shed the bond by being rebuilt from their board or flatpacked.
/// </summary>
[TestFixture]
[TestOf(typeof(WFHullBondSystem))]
public sealed class HullBondTest
{
    private const string Drive = "MachineFTLDrive";
    private const string Crane = "WFMobileDrydockCrane";
    private const string Console = "WFComputerMobileDrydock";
    private const string ConsoleBoard = "WFComputerMobileDrydockCircuitboard";
    private const string Flatpacker = "MachineFlatpacker";

    /// <summary>Creates a named 4x4 plated hull.</summary>
    private static EntityUid CreateHull(TestPair pair, MapId map, Vector2 pos, string name)
    {
        var server = pair.Server;
        var maps = server.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(map);
        var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                maps.SetTile(grid, new Vector2i(x, y), plating);
            }
        }

        server.System<SharedTransformSystem>().SetWorldPosition(grid, pos);
        server.System<MetaDataSystem>().SetEntityName(grid, name);
        return grid;
    }

    private static EntityCoordinates At(EntityUid hull, int x, int y)
    {
        return new EntityCoordinates(hull, x + 0.5f, y + 0.5f);
    }

    /// <summary>Unbolts an entity and sets it down on a hull's tile.</summary>
    private static void Carry(TestPair pair, EntityUid uid, EntityUid hull, int x, int y)
    {
        var xforms = pair.Server.System<SharedTransformSystem>();
        if (pair.Server.EntMan.GetComponent<TransformComponent>(uid).Anchored)
            xforms.Unanchor(uid);

        xforms.SetCoordinates(uid, At(hull, x, y));
    }

    /// <summary>Whether a wrench would be refused where the entity stands.</summary>
    private static bool Refused(TestPair pair, EntityUid uid)
    {
        var ev = new AnchorAttemptEvent(uid, uid);
        pair.Server.EntMan.EventBus.RaiseLocalEvent(uid, ev);
        return ev.Cancelled;
    }

    /// <summary>Bolts an entity down where it stands, as a finished wrench does.</summary>
    private static void Bolt(TestPair pair, EntityUid uid)
    {
        pair.Server.System<SharedTransformSystem>().AnchorEntity(uid);
        pair.Server.EntMan.EventBus.RaiseLocalEvent(uid, new UserAnchoredEvent(uid, uid));
    }

    private static string? HullId(TestPair pair, EntityUid hull)
    {
        return pair.Server.EntMan.TryGetComponent(hull, out WFHullBondIdComponent? id) ? id.Id : null;
    }

    /// <summary>The one entity on a hull with both components.</summary>
    private static EntityUid SingleOn<T1, T2>(TestPair pair, EntityUid hull)
        where T1 : IComponent
        where T2 : IComponent
    {
        var found = new List<EntityUid>();
        var query = pair.Server.EntMan.EntityQueryEnumerator<T1, T2, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid == hull)
                found.Add(uid);
        }

        Assert.That(found, Has.Count.EqualTo(1), $"Expected one {typeof(T1).Name} on the hull.");
        return found[0];
    }

    [Test]
    public async Task FittingOnlyBoltsToItsHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var alpha = CreateHull(pair, map.MapId, new Vector2(40, 40), "Alpha");
            var bravo = CreateHull(pair, map.MapId, new Vector2(60, 60), "Bravo");

            var drive = entMan.SpawnEntity(Drive, At(alpha, 1, 1));
            var bond = entMan.GetComponent<WFHullBondComponent>(drive);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(drive).Anchored, "The drive should spawn bolted down.");
                Assert.That(bond.Hull, Is.Not.Null.And.EqualTo(HullId(pair, alpha)), "A fitting spawned bolted down bonds to its hull.");
                Assert.That(bond.HullName, Is.EqualTo("Alpha"));
            });

            Carry(pair, drive, alpha, 2, 2);
            Assert.That(Refused(pair, drive), Is.False, "A fitting moves freely around its own hull.");

            Carry(pair, drive, bravo, 1, 1);
            Assert.That(Refused(pair, drive), Is.True, "A fitting must not bolt down on another hull.");

            // Dragged across while the wrench was turning: the attempt was checked on its own hull.
            Bolt(pair, drive);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(drive).Anchored, Is.False, "A fitting bolted to another hull comes loose.");
                Assert.That(bond.Hull, Is.EqualTo(HullId(pair, alpha)), "The bond must not move with the fitting.");
            });

            Carry(pair, drive, alpha, 1, 1);
            Bolt(pair, drive);
            Assert.That(entMan.GetComponent<TransformComponent>(drive).Anchored, "The fitting should bolt back down on its hull.");

            // A piece broken off the hull is still that hull.
            Carry(pair, drive, bravo, 1, 1);
            var split = new GridSplitEvent(new[] { bravo }, alpha);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref split);
            Assert.That(Refused(pair, drive), Is.False, "A fitting should bolt down on a piece split off its hull.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LooseHullFittingsBondElsewhere()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var hullBond = server.System<WFHullBondSystem>();

        await server.WaitAssertion(() =>
        {
            var raider = CreateHull(pair, map.MapId, new Vector2(40, 40), "Raider");
            var bravo = CreateHull(pair, map.MapId, new Vector2(60, 60), "Bravo");

            var drive = entMan.SpawnEntity(Drive, At(raider, 1, 1));
            var bond = entMan.GetComponent<WFHullBondComponent>(drive);
            Assert.That(bond.Hull, Is.Not.Null, "The drive bonds to the hull it spawned on.");

            // An encounter ship is loosened once its hull is built.
            hullBond.Loosen(raider);
            Assert.That(bond.Hull, Is.Null, "Loosening a hull frees the fittings bonded to it.");

            var shield = entMan.SpawnEntity(Drive, At(raider, 2, 2));
            Assert.That(entMan.GetComponent<WFHullBondComponent>(shield).Hull, Is.Null, "Nothing bonds to a loose hull.");

            Carry(pair, drive, bravo, 1, 1);
            Assert.That(Refused(pair, drive), Is.False, "A fitting stripped off a loose hull bolts down on another.");
            Bolt(pair, drive);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(drive).Anchored, "The stolen fitting stays bolted down.");
                Assert.That(bond.Hull, Is.Not.Null.And.EqualTo(HullId(pair, bravo)), "It bonds to the hull it was bolted to.");
            });

            Carry(pair, drive, raider, 1, 1);
            Assert.That(Refused(pair, drive), Is.True, "Once bonded elsewhere, it must not go back on the loose hull.");

            // A piece broken off a loose hull stays loose.
            var split = new GridSplitEvent(new[] { bravo }, raider);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref split);
            Assert.That(entMan.GetComponent<WFHullBondIdComponent>(bravo).Loose, "A piece split off a loose hull is loose.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnboltedFittingBondsWhereFirstBolted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var alpha = CreateHull(pair, map.MapId, new Vector2(40, 40), "Alpha");
            var bravo = CreateHull(pair, map.MapId, new Vector2(60, 60), "Bravo");

            var crane = entMan.SpawnEntity(Crane, At(alpha, 1, 2));
            var bond = entMan.GetComponent<WFHullBondComponent>(crane);
            Assert.That(bond.Hull, Is.Null, "A fitting that was never bolted down has no bond.");

            Carry(pair, crane, bravo, 1, 2);
            Assert.That(Refused(pair, crane), Is.False, "An unbonded fitting bolts down anywhere.");
            Bolt(pair, crane);
            Assert.That(bond.Hull, Is.Not.Null.And.EqualTo(HullId(pair, bravo)), "A fitting bonds to the first hull it is bolted to.");

            Carry(pair, crane, alpha, 1, 2);
            Assert.That(Refused(pair, crane), Is.True, "Once bonded, a fitting must not bolt down on another hull.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RebuiltFittingKeepsItsBond()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var construction = server.System<ConstructionSystem>();
        var containers = server.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var alpha = CreateHull(pair, map.MapId, new Vector2(40, 40), "Alpha");
            var bravo = CreateHull(pair, map.MapId, new Vector2(60, 60), "Bravo");
            var drive = entMan.SpawnEntity(Drive, At(alpha, 1, 1));
            var id = entMan.GetComponent<WFHullBondComponent>(drive).Hull;
            Assert.That(id, Is.Not.Null);

            // Pried apart: the board carries the bond.
            Assert.That(construction.ChangeNode(drive, null, "machineFrame"), "The drive should come apart into a frame.");
            var frame = SingleOn<MachineFrameComponent, ConstructionComponent>(pair, alpha);
            var board = containers.GetContainer(frame, MachineFrameComponent.BoardContainerName).ContainedEntities.Single();
            Assert.That(entMan.TryGetComponent(board, out WFHullBondComponent? boardBond) && boardBond.Hull == id,
                "A bonded fitting's board should carry its bond.");

            // Rebuilt on another hull: construction bolts it down, the bond lets go.
            Carry(pair, frame, bravo, 1, 1);
            server.System<SharedTransformSystem>().AnchorEntity(frame);
            Assert.That(construction.ChangeNode(frame, null, "machine"), "The frame should build back into a drive.");
            var rebuilt = SingleOn<WFHullBondComponent, MachineComponent>(pair, bravo);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<WFHullBondComponent>(rebuilt).Hull, Is.EqualTo(id),
                    "A fitting built from a bonded board keeps the board's bond.");
                Assert.That(entMan.GetComponent<TransformComponent>(rebuilt).Anchored, Is.False,
                    "A fitting rebuilt on another hull must not stay bolted down.");
                Assert.That(Refused(pair, rebuilt), Is.True);
            });

            Carry(pair, rebuilt, alpha, 2, 2);
            Assert.That(Refused(pair, rebuilt), Is.False, "The rebuilt fitting still bolts down on its own hull.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The mobile drydock crane spawns loose, so an SRD rebuild once left it unbolted and unbonded, free to carry to
    /// another ship.
    /// </summary>
    [Test]
    public async Task SrdRebuildsFittingBoltedAndBonded()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var alpha = EntityUid.Invalid;
        var bravo = EntityUid.Invalid;
        var user = EntityUid.Invalid;
        var tool = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            alpha = CreateHull(pair, map.MapId, new Vector2(40, 40), "Alpha");
            bravo = CreateHull(pair, map.MapId, new Vector2(60, 60), "Bravo");
            var crane = entMan.SpawnEntity(Crane, At(alpha, 1, 2));
            Bolt(pair, crane);
            server.System<ShipRepairSystem>().GenerateRepairData(alpha);
            entMan.DeleteEntity(crane);

            user = entMan.SpawnEntity("MobHuman", At(alpha, 0, 0));
            tool = entMan.SpawnEntity("ShipRepairDeviceRecharging", At(alpha, 0, 0));
            Assert.That(server.System<SharedHandsSystem>().TryPickupAnyHand(user, tool), Is.True);
            entMan.GetComponent<ShipRepairToolComponent>(tool).RepairTimeMultiplier = 0.03f;
        });
        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
            entMan.EventBus.RaiseLocalEvent(tool, new AfterInteractEvent(user, tool, null, At(alpha, 1, 2), true)));
        await server.WaitRunTicks(pair.SecondsToTicks(1f));

        await server.WaitAssertion(() =>
        {
            var rebuilt = SingleOn<WFHullBondComponent, MetaDataComponent>(pair, alpha);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<TransformComponent>(rebuilt).Anchored, Is.True,
                    "The SRD should rebuild a fitting bolted down, as the snapshot had it.");
                Assert.That(entMan.GetComponent<WFHullBondComponent>(rebuilt).Hull, Is.Not.Null.And.EqualTo(HullId(pair, alpha)),
                    "A fitting the SRD rebuilds should bond to the hull it was rebuilt on.");
            });

            Carry(pair, rebuilt, bravo, 1, 2);
            Assert.That(Refused(pair, rebuilt), Is.True, "A rebuilt fitting must not bolt down on another hull.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task BondedBoardWontFlatpack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var containers = server.System<SharedContainerSystem>();
        var slots = server.System<ItemSlotsSystem>();

        await server.WaitAssertion(() =>
        {
            var alpha = CreateHull(pair, map.MapId, new Vector2(40, 40), "Alpha");
            var console = entMan.SpawnEntity(Console, At(alpha, 1, 1));
            if (!entMan.GetComponent<TransformComponent>(console).Anchored)
                server.System<SharedTransformSystem>().AnchorEntity(console);

            var container = containers.GetContainer(console, "board");
            var board = container.ContainedEntities.Single();
            Assert.That(containers.Remove(board, container), "The console should give up its board.");
            Assert.That(entMan.TryGetComponent(board, out WFHullBondComponent? bond) && bond.Hull == HullId(pair, alpha),
                "A board taken out of a bolted-down fitting carries the hull's bond.");

            var packer = entMan.SpawnEntity(Flatpacker, At(alpha, 3, 3));
            Assert.That(slots.TryGetSlot(packer, entMan.GetComponent<FlatpackCreatorComponent>(packer).SlotId, out var slot));
            var fresh = entMan.SpawnEntity(ConsoleBoard, At(alpha, 0, 0));
            Assert.Multiple(() =>
            {
                Assert.That(slots.CanInsert(packer, fresh, null, slot!), Is.True, "A new board should flatpack.");
                Assert.That(slots.CanInsert(packer, board, null, slot!), Is.False, "A bonded board must not flatpack into an unbonded machine.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
