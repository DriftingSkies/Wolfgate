using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Shared._WF.Lattice;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>The Coda's hazards work on a ship: hunters board, structure rots, and failed power comes back.</summary>
public sealed class WFCodaHazardTest
{
    [Test]
    public async Task HazardsHitAShip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var hazards = entities.System<WFCodaHazardSystem>();
        var maps = entities.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var profile = protos.Index<WFLatticeProfilePrototype>("WFDefault");
            Assert.That(profile.HunterMobs, Is.Not.Empty, "the Coda has nothing to hunt with.");

            var grid = (map.Grid.Owner, entities.GetComponent<MapGridComponent>(map.Grid));
            for (var x = -6; x <= 6; x++)
            {
                maps.SetTile(map.Grid, new Vector2i(x, 0), maps.GetTileRef(map.Grid, entities.GetComponent<MapGridComponent>(map.Grid), new Vector2i(0, 0)).Tile);
            }

            Assert.That(hazards.Board(grid, 2, profile), Is.EqualTo(2), "no hunters boarded.");
            var hunters = profile.HunterMobs.Select(id => id.Id).ToHashSet();
            var remade = entities.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype is { } proto && hunters.Contains(proto.ID));
            Assert.That(remade, Is.GreaterThanOrEqualTo(2));

            var wall = entities.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, new Vector2(3.5f, 0.5f)));
            for (var i = 0; i < 40; i++)
            {
                hazards.RotNow(grid, 1f, profile);
            }

            Assert.That(entities.Deleted(wall) || entities.GetComponent<DamageableComponent>(wall).TotalDamage > 0, Is.True,
                "the Coda didn't eat the hull.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A crewed ship flown into the Coda is counted at once, its power flickers and its doors move, and an arm twists
    /// into a blade that can't be let go.
    /// </summary>
    [Test]
    public async Task ACrewedShipInTheCodaFalls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var players = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var hazards = entities.System<WFCodaHazardSystem>();

        EntityUid mob = default, apc = default, door = default;
        await server.WaitAssertion(() =>
        {
            entities.System<WFLatticeSystem>().Setup(map.MapUid);
            entities.EnsureComponent<Content.Server.Shuttles.Components.ShuttleComponent>(map.Grid);
            entities.System<SharedTransformSystem>().SetWorldPosition(map.Grid.Owner, new Vector2(72000f, 0f));
            var maps = entities.System<SharedMapSystem>();
            var gridComp = entities.GetComponent<MapGridComponent>(map.Grid);
            var floor = maps.GetTileRef(map.Grid, gridComp, new Vector2i(0, 0)).Tile;
            for (var x = -3; x <= 3; x++)
            {
                maps.SetTile(map.Grid, new Vector2i(x, 0), floor);
            }

            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), mob, force: true);
            apc = entities.SpawnEntity("APCBasic", map.GridCoords.Offset(new Vector2(2f, 0f)));
            door = entities.SpawnEntity("AirlockGlass", map.GridCoords.Offset(new Vector2(-2f, 0f)));
            // No APC feeds it here; a powered door is what the fault acts on.
            entities.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(door).NeedsPower = false;
        });

        await pair.RunSeconds(7f);
        await server.WaitAssertion(() =>
        {
            Assert.That(hazards.IsExposed(map.Grid.Owner), Is.True, "a crewed ship in the Coda isn't counted.");
            Assert.That(hazards.FailNow(map.Grid.Owner, 3, 5f), Is.True, "the power didn't flicker.");
            Assert.That(entities.HasComponent<WFCodaFlutterComponent>(apc), Is.True);
            Assert.That(hazards.FailNow(map.Grid.Owner, 4, 5f), Is.True, "no door moved.");
            var doorState = entities.GetComponent<Content.Shared.Doors.Components.DoorComponent>(door).State;
            var bolted = entities.TryGetComponent<Content.Shared.Doors.Components.DoorBoltComponent>(door, out var bolts) && bolts.BoltsDown;
            Assert.That(bolted || doorState != Content.Shared.Doors.Components.DoorState.Closed, Is.True, "the ship's only door neither moved nor bolted.");

            var profile = protos.Index<WFLatticeProfilePrototype>("WFDefault");
            Assert.That(entities.System<WFCodaMutationSystem>().Mutate(mob, profile), Is.True, "the arm didn't twist.");
            var blade = entities.GetComponent<WFCodaMutatedComponent>(mob).Blade;
            Assert.That(entities.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().IsHolding(mob, blade), Is.True,
                "the blade isn't in a hand.");
            Assert.That(entities.HasComponent<Content.Shared.Interaction.Components.UnremoveableComponent>(blade), Is.True);
        });

        // The flicker ends and the APC carries on.
        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<WFCodaFlutterComponent>(apc), Is.False, "the flicker never ended.");
            Assert.That(entities.GetComponent<Content.Server.Power.Components.PowerNetworkBatteryComponent>(apc).CanDischarge, Is.True,
                "the APC was left cut off.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Flesh roots on a crewed ship in the Coda and creeps out; the leviathan passes, touches the hull and leaves its mark.</summary>
    [Test]
    public async Task FleshAndTheLeviathan()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var protos = server.ResolveDependency<IPrototypeManager>();
        var players = server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        var hazards = entities.System<WFCodaHazardSystem>();
        var flesh = entities.System<WFCodaFleshSystem>();
        var maps = entities.System<SharedMapSystem>();

        EntityUid mob = default;
        var tilesBefore = 0;
        await server.WaitAssertion(() =>
        {
            entities.System<WFLatticeSystem>().Setup(map.MapUid);
            entities.EnsureComponent<Content.Server.Shuttles.Components.ShuttleComponent>(map.Grid);
            entities.System<SharedTransformSystem>().SetWorldPosition(map.Grid.Owner, new Vector2(72000f, 0f));

            var gridComp = entities.GetComponent<MapGridComponent>(map.Grid);
            var floor = maps.GetTileRef(map.Grid, gridComp, new Vector2i(0, 0)).Tile;
            for (var x = -12; x <= 12; x++)
            {
                for (var y = -1; y <= 1; y++)
                {
                    maps.SetTile(map.Grid, new Vector2i(x, y), floor);
                }
            }

            // A spur off the hull for the leviathan to tear away.
            maps.SetTile(map.Grid, new Vector2i(12, 2), floor);
            tilesBefore = maps.GetAllTiles(map.Grid, gridComp).Count();

            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), mob, force: true);
        });

        await pair.RunSeconds(7f);
        await server.WaitAssertion(() =>
        {
            Assert.That(hazards.IsExposed(map.Grid.Owner), Is.True);
            var grid = (map.Grid.Owner, entities.GetComponent<MapGridComponent>(map.Grid));
            Assert.That(flesh.Seed(grid, new System.Collections.Generic.List<EntityUid> { mob }), Is.True, "no flesh took root.");
            Assert.That(flesh.Nodes(map.Grid.Owner), Is.EqualTo(1));
            hazards.StartLeviathan(map.Grid.Owner, protos.Index<WFLatticeProfilePrototype>("WFDefault"), touch: true);
        });

        await pair.RunSeconds(47f);
        await server.WaitAssertion(() =>
        {
            // The Coda may root another knot of its own meanwhile; the seeded one has had the longest to grow.
            var grown = entities.EntityQuery<WFCodaFleshNodeComponent>().Max(node => node.Tiles.Count);
            Assert.That(grown, Is.GreaterThan(3), "the flesh didn't creep out from its node.");

            var gridComp = entities.GetComponent<MapGridComponent>(map.Grid);
            var tilesAfter = maps.GetAllTiles(map.Grid, gridComp).Count();
            Assert.That(tilesAfter, Is.LessThan(tilesBefore), "the leviathan's touch tore nothing away.");
            Assert.That(tilesBefore - tilesAfter, Is.LessThanOrEqualTo(3), "the leviathan tore off more than it should.");
            Assert.That(entities.GetComponent<WFDriftComponent>(mob).Mind, Is.GreaterThan(0f), "the leviathan left nothing in the crew's heads.");
            Assert.That(entities.Deleted(mob), Is.False);

            flesh.Clear(map.Grid.Owner);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(flesh.Nodes(map.Grid.Owner), Is.Zero, "the flesh wasn't stripped.");
        });

        await pair.CleanReturnAsync();
    }
}