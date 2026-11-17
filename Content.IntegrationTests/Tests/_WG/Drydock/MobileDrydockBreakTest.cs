using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._Mono.ShipRepair;
using Content.Server._WG.Drydock;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Lathe;
using Content.Shared.Construction.Components;
using Content.Shared._WG.Drydock;
using Content.Shared.Maps;
using Content.Shared.Materials;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Drydock;

/// <summary>Tests hull breaking at a mobile drydock: the peel, its yield and caps, refusals, aborts and people aboard.</summary>
[TestFixture]
public sealed class MobileDrydockBreakTest
{
    private static readonly EntProtoId MachineFTLDrive25SProto = "MachineFTLDrive25S";
    private static readonly ProtoId<ContentTileDefinition> PlatingProto = "Plating";

    private const int HullSize = 4;

    /// <summary>A hardpoint gun, salvaged whole.</summary>
    private const string Gun = "WeaponLaserTurretL1Phalanx";

    /// <summary>Builds a mobile yard with a 4x4 hull in its berth carrying two plastitanium walls, and an actor.</summary>
    private static (EntityUid Console, EntityUid Crane, EntityUid Ship, EntityUid Actor) SetUpBreak(TestPair pair, MapId map)
    {
        var yard = MobileDrydockTest.SpawnMobileYard(pair, map);
        var comp = pair.Server.EntMan.GetComponent<WGDrydockConsoleComponent>(yard.Console);
        Assert.That(pair.Server.System<WGDrydockSystem>().TryGetBerth(yard.Console, comp, out var berth), "The crane should make a berth.");

        var ship = DrydockTest.CreateShip(pair, map);
        DrydockTest.SetFloor(pair, ship, HullSize);
        DrydockTest.SpawnAnchored(pair, ship, "WallPlastitanium", new Vector2i(0, 0));
        DrydockTest.SpawnAnchored(pair, ship, "WallPlastitanium", new Vector2i(3, 3));
        DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.Center);

        var actor = pair.Server.EntMan.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map));
        return (yard.Console, yard.Crane, ship, actor);
    }

    /// <summary>Ticks until a console's job ends.</summary>
    private static async Task RunBreak(TestPair pair, EntityUid console)
    {
        var em = pair.Server.EntMan;
        await DrydockTest.RunUntil(pair, 400, 30, () => !em.HasComponent<WGDrydockJobComponent>(console));
        Assert.That(em.HasComponent<WGDrydockJobComponent>(console), Is.False, "The break should end.");
    }

    /// <summary>A crane's own stock of a material.</summary>
    private static int Stored(TestPair pair, EntityUid crane, string material)
    {
        return pair.Server.System<SharedMaterialStorageSystem>().GetMaterialAmount(crane, material, localOnly: true);
    }

    /// <summary>
    /// A break peels the whole hull, pays plastitanium and steel into the crane, and casts a
    /// person standing aboard into space instead of deleting them with the grid. Its gun goes to the parts silo as a
    /// salvaged flatpack, its autolathe's parts go there too and its board to the crane, and its window pays glass.
    /// </summary>
    [Test]
    public async Task BreakPeelsHullAndPaysYield()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();

        EntityUid console = default, crane = default, ship = default, actor = default, aboard = default, silo = default;
        await server.WaitAssertion(() =>
        {
            (console, crane, ship, actor) = SetUpBreak(pair, map.MapId);
            aboard = em.SpawnEntity("MobHuman", new EntityCoordinates(ship, 1.5f, 1.5f));
            DrydockTest.SpawnAnchored(pair, ship, Gun, new Vector2i(2, 1));
            DrydockTest.SpawnAnchored(pair, ship, "Autolathe", new Vector2i(2, 2));
            DrydockTest.SpawnAnchored(pair, ship, "ReinforcedWindow", new Vector2i(1, 0));
            silo = MobileDrydockSiloTest.SpawnLinkedSilo(pair, em.GetComponent<TransformComponent>(console).GridUid!.Value,
                "WGMachinePartsSilo", new Vector2i(5, 2), crane);
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartBreak(console, comp, ship, actor), Is.True, "The break should start.");
            Assert.That(em.GetComponent<WGDrydockJobComponent>(console).Total, Is.EqualTo(HullSize * HullSize),
                "The break should take one item per tile.");
        });

        await RunBreak(pair, console);

        await server.WaitAssertion(() =>
        {
            var tiles = HullSize * HullSize;
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(ship), Is.True, "The hull should be gone with its last tile.");
                Assert.That(em.Deleted(aboard), Is.False, "The person aboard should not be deleted with the hull.");
                Assert.That(em.GetComponent<TransformComponent>(aboard).MapID, Is.EqualTo(map.MapId),
                    "The person aboard should be adrift on the map.");
                Assert.That(Stored(pair, crane, "Plastitanium"), Is.EqualTo(16),
                    "The crane should get 2% of each plastitanium wall's 4 sheets.");
                Assert.That(Stored(pair, crane, "Steel"), Is.InRange(tiles * 2, tiles * 100),
                    "The crane should get steel for every tile.");
                Assert.That(Stored(pair, crane, "RawScrap"), Is.EqualTo(tiles * 200),
                    "The crane should get two scrap chunks for every tile.");

                var packs = em.GetComponent<FabricationSiloComponent>(silo).Parts!.ContainedEntities
                    .Where(e => em.TryGetComponent(e, out FlatpackComponent pack) && pack.Entity == Gun)
                    .ToList();
                Assert.That(packs, Has.Count.EqualTo(1), "The gun should be in the parts silo as a salvaged flatpack.");

                var siloed = em.GetComponent<FabricationSiloComponent>(silo).Parts!.ContainedEntities
                    .Count(e => em.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID == "MatterBinStockPart");
                Assert.That(siloed, Is.GreaterThan(0), "The autolathe's matter bins should be in the parts silo.");

                var yard = em.GetComponent<TransformComponent>(crane).GridUid;
                var boards = new List<EntityUid>();
                var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var meta, out var xform))
                {
                    if (meta.EntityPrototype?.ID == "AutolatheMachineCircuitboard" && (xform.GridUid == yard || xform.ParentUid == silo))
                        boards.Add(uid);
                }

                Assert.That(boards, Has.Count.EqualTo(1), "The autolathe's board should come to the crane.");
                Assert.That(Stored(pair, crane, "ReinforcedGlass"), Is.GreaterThan(0), "The window should pay reinforced glass.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A jump drive's board sheets, its fissile uranium among them, count as its construction materials: they price its
    /// rebuild, and a break previews and pays half of them.
    /// </summary>
    [Test]
    public async Task MachineSheetsCountAsConstruction()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();

        EntityUid console = default, crane = default, ship = default, actor = default;
        await server.WaitAssertion(() =>
        {
            (console, crane, ship, actor) = SetUpBreak(pair, map.MapId);
            DrydockTest.SpawnAnchored(pair, ship, "MachineFTLDrive25S", new Vector2i(1, 2));
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            drydock.UpdateUi(console, force: true);
            Assert.That(server.System<SharedUserInterfaceSystem>().TryGetUiState<WGDrydockConsoleState>(console,
                WGDrydockConsoleUiKey.Key, out var state), Is.True, "The console should have a state.");
            Assert.That(state!.BreakYield.GetValueOrDefault("UraniumFissile"), Is.EqualTo(100),
                "The salvage preview should list half of the drive's fissile.");
            Assert.That(drydock.RebuildPrice(new WGDrydockConsoleComponent(), server.ProtoMan.Index(MachineFTLDrive25SProto)),
                Is.AtLeast(7000), "Rebuilding the drive should be priced on its fissile.");

            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartBreak(console, comp, ship, actor), Is.True, "The break should start.");
        });

        await RunBreak(pair, console);

        await server.WaitAssertion(() =>
        {
            Assert.That(Stored(pair, crane, "UraniumFissile"), Is.EqualTo(100), "Half of the drive's fissile should be stored.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull hanging off the corner of the berth, its centre outside, is still found in it, and loses every tile the
    /// crane reaches while keeping those it doesn't.
    /// </summary>
    [Test]
    public async Task BreakStripsReachablePartOfOverhangingHull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();
        var maps = server.System<SharedMapSystem>();
        const int size = 8;

        EntityUid console = default, crane = default, ship = default;
        await server.WaitAssertion(() =>
        {
            var yard = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            (console, crane) = (yard.Console, yard.Crane);
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out var berth), "The crane should make a berth.");

            ship = DrydockTest.CreateShip(pair, map.MapId);
            DrydockTest.SetFloor(pair, ship, size);
            DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.TopRight + new Vector2(2f, 2f));
            Assert.That(drydock.GetBerthedShip(console, comp), Is.EqualTo(ship),
                "A ship with tiles in the berth should be found there, wherever its centre is.");
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            var actor = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map.MapId));
            Assert.That(drydock.TryStartBreak(console, comp, ship, actor), Is.True, "The break should start.");
        });

        await RunBreak(pair, console);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            var left = maps.GetAllTiles(ship, em.GetComponent<MapGridComponent>(ship)).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(left.Count, Is.InRange(1, size * size - 1), "The hull should be partly broken.");
                foreach (var tile in left)
                {
                    var centre = new Vector2(tile.GridIndices.X + 0.5f, tile.GridIndices.Y + 0.5f);
                    Assert.That(drydock.CanReach(comp, ship, crane, centre), Is.False,
                        $"Tile {tile.GridIndices} is in reach and should have been lifted.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull of one-tile-wide loops and a solid corner breaks to nothing without ever splitting into a second grid:
    /// loop tiles whose sides only meet the long way round, and bridge tiles that would cut the hull, wait their turn.
    /// </summary>
    [Test]
    public async Task BreakNeverSplitsHull()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();
        var maps = server.System<SharedMapSystem>();
        const int size = 5;

        int GridCount()
        {
            var count = 0;
            var query = em.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapID == map.MapId)
                    count++;
            }

            return count;
        }

        EntityUid console = default, crane = default, ship = default;
        var grids = 0;
        await server.WaitAssertion(() =>
        {
            var yard = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            (console, crane) = (yard.Console, yard.Crane);
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out var berth), "The crane should make a berth.");

            // A 5x5 ring with a cross bar through its middle and a filled 2x2 corner.
            ship = DrydockTest.CreateShip(pair, map.MapId);
            var plating = new Tile(server.ProtoMan.Index<ContentTileDefinition>(PlatingProto).TileId);
            var tiles = new List<(Vector2i, Tile)>();
            for (var x = 0; x < size; x++)
            for (var y = 0; y < size; y++)
            {
                if (x == 0 || y == 0 || x == size - 1 || y == size - 1 || y == size / 2 || x < 2 && y < 2)
                    tiles.Add((new Vector2i(x, y), plating));
            }

            maps.SetTiles(ship, em.GetComponent<MapGridComponent>(ship), tiles);
            DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.Center);
            grids = GridCount();
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            var actor = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map.MapId));
            Assert.That(drydock.TryStartBreak(console, comp, ship, actor), Is.True, "The break should start.");
        });

        var most = 0;
        for (var i = 0; i < 400 && em.HasComponent<WGDrydockJobComponent>(console); i++)
        {
            await server.WaitRunTicks(30);
            await server.WaitPost(() => most = Math.Max(most, GridCount()));
        }

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WGDrydockJobComponent>(console), Is.False, "The break should end.");
                Assert.That(most, Is.LessThanOrEqualTo(grids), "The hull should never split into another grid.");
                Assert.That(em.Deleted(ship), Is.True, "The whole hull should be broken.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A crane destroyed mid-break leaves the job to the other crane instead of jamming it.</summary>
    [Test]
    public async Task BreakSurvivesDestroyedCrane()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();

        EntityUid console = default, crane = default, second = default, ship = default, actor = default;
        await server.WaitAssertion(() =>
        {
            (console, crane, ship, actor) = SetUpBreak(pair, map.MapId);
            var yard = em.GetComponent<TransformComponent>(console).GridUid!.Value;
            second = DrydockTest.SpawnAnchored(pair, yard, "WGMobileDrydockCrane", new Vector2i(2, 5));
            DrydockTest.LinkCrane(pair, console, second);
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);
        await MobileDrydockTest.WaitForCranePower(pair, second);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartBreak(console, comp, ship, actor), Is.True, "The break should start.");
        });

        bool Holding() => em.TryGetComponent(console, out WGDrydockJobComponent job) &&
                          job.Slots.Any(s => s.Crane == second && s.Item != null);

        await DrydockTest.RunUntil(pair, 200, 5, Holding);
        await server.WaitAssertion(() =>
        {
            Assert.That(Holding(), Is.True, "The second crane should take a tile.");
            em.DeleteEntity(second);
        });

        await RunBreak(pair, console);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(ship), Is.True, "The other crane should finish breaking the hull.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An outpost, a station member without a deed, is refused; a bought ship, which carries one, is not.</summary>
    [Test]
    public async Task BreakRefusesOutposts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            var (console, _, ship, actor) = SetUpBreak(pair, map.MapId);
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            em.AddComponent<StationMemberComponent>(ship);

            Assert.That(drydock.BreakRefusal(console, comp, ship), Is.Not.Null, "An outpost should be refused.");
            Assert.That(drydock.TryStartBreak(console, comp, ship, actor), Is.False, "An outpost should not be broken.");
            Assert.That(em.HasComponent<WGDrydockJobComponent>(console), Is.False, "No job should start on an outpost.");

            em.AddComponent<ShuttleDeedComponent>(ship);
            Assert.That(drydock.BreakRefusal(console, comp, ship), Is.Null, "A deeded ship should be breakable.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A hull with an indestructible wall or monolith tenebris is refused; one with a gaslock, which takes no damage at
    /// all, is not.
    /// </summary>
    [Test]
    public async Task BreakRefusesIndestructibleHulls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            var (console, _, ship, _) = SetUpBreak(pair, map.MapId);
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            var gaslock = DrydockTest.SpawnAnchored(pair, ship, "Gaslock", new Vector2i(1, 1));
            Assert.That(drydock.BreakRefusal(console, comp, ship), Is.Null, "A gaslock should not protect a hull.");
            em.DeleteEntity(gaslock);

            foreach (var wall in new[] { "WallPlastitaniumIndestructible", "WallReinforcedChitinMonolithic" })
            {
                var uid = DrydockTest.SpawnAnchored(pair, ship, wall, new Vector2i(1, 1));
                Assert.That(drydock.BreakRefusal(console, comp, ship), Is.Not.Null, $"A hull with {wall} should be refused.");
                em.DeleteEntity(uid);
            }
        });

        await pair.CleanReturnAsync();
    }
}
