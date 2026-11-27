#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._ES.TileFires;
using Content.Server._WG.GroundFire;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._ES.TileFires;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WG.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WG.GroundFire;

/// <summary>A fire on planet ground stays local and burns out; it used to spread without limit.</summary>
[TestFixture]
public sealed class GroundFireTest
{
    private const string Grass = "FloorPlanetGrass";
    /// <summary>A built floor the planet's atmosphere tracks, so a fire can see its neighbours.</summary>
    private const string Wood = "FloorWood";
    private const string Tree = "FloraTreeCrocusPine";

    /// <summary>Most tile fires a single ground fire may ever have at once, with a little room over the design limit.</summary>
    private const int FireCap = 16;

    /// <summary>Furthest a ground fire may reach from where it started, in tiles.</summary>
    private const int ReachCap = 12;

    private static IEnumerable<Vector2i> Area(Vector2i from, Vector2i to)
    {
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
            yield return new Vector2i(x, y);
    }

    /// <summary>Open grass with trees on it, lit at the middle: the burned area stays bounded and the fire ends.</summary>
    [TestCase(Grass)]
    [TestCase(Wood)]
    public async Task AGroundFireStaysLocalAndEnds(string floor)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var fires = server.System<ESTileFireSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableFeature(pair);
        var ground = (await BuildStandalone(pair))[0];
        var peak = 0;
        var reach = 0f;

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var grass = new Tile(tileDefs[floor].TileId);
            maps.SetTiles(ground, grid, Area(new Vector2i(-40, -40), new Vector2i(40, 40)).Select(i => (i, grass)).ToList());

            // A tree on every fourth tile, leaving the rest open for a fire to cross.
            foreach (var index in Area(new Vector2i(-40, -40), new Vector2i(40, 40)))
            {
                if ((index.X + index.Y * 3) % 4 == 0 && index != Vector2i.Zero)
                    entMan.SpawnEntity(Tree, new EntityCoordinates(ground, index.X + 0.5f, index.Y + 0.5f));
            }
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitPost(() =>
        {
            Assert.That(fires.TryDoTileFire(new EntityCoordinates(ground, 0.5f, 0.5f), stage: 4), Is.True);
        });

        for (var second = 0; second < 240; second++)
        {
            await server.WaitRunTicks(pair.SecondsToTicks(1f));
            await server.WaitPost(() =>
            {
                var count = 0;
                var query = entMan.EntityQueryEnumerator<ESTileFireComponent, TransformComponent>();
                while (query.MoveNext(out _, out _, out var xform))
                {
                    count++;
                    reach = MathF.Max(reach, MathF.Max(MathF.Abs(xform.LocalPosition.X), MathF.Abs(xform.LocalPosition.Y)));
                }

                peak = Math.Max(peak, count);
            });
        }

        await server.WaitAssertion(() =>
        {
            var left = entMan.Count<ESTileFireComponent>();
            var burning = 0;
            var flammables = entMan.EntityQueryEnumerator<FlammableComponent>();
            while (flammables.MoveNext(out _, out var flammable))
            {
                if (flammable.OnFire)
                    burning++;
            }

            Console.WriteLine($"GROUNDFIRE peak={peak} reach={reach} left={left} burning={burning}");
            Assert.That(peak, Is.LessThanOrEqualTo(FireCap), "The ground fire grew past its limit.");
            Assert.That(reach, Is.LessThanOrEqualTo(ReachCap), "The ground fire spread too far.");
            Assert.That(left, Is.Zero, "The ground fire never ended.");
            Assert.That(burning, Is.Zero, "Something kept burning after the ground fire ended.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A fire lit beside a spreading ground fire joins its line; one lit further off starts its own.</summary>
    [Test]
    public async Task AFireLitBesideAnotherAddsNoSpread()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var fires = server.System<ESTileFireSystem>();
        var groundFires = server.System<WGGroundFireSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableFeature(pair);
        var ground = (await BuildStandalone(pair))[0];

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var grass = new Tile(tileDefs[Grass].TileId);
            maps.SetTiles(ground, grid, Area(new Vector2i(-2, -2), new Vector2i(12, 2)).Select(i => (i, grass)).ToList());
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            foreach (var x in new[] { 0, 2, 10 })
                Assert.That(fires.TryDoTileFire(new EntityCoordinates(ground, x + 0.5f, 0.5f)), Is.True);

            Entity<ESTileFireComponent> At(int x)
            {
                var query = entMan.EntityQueryEnumerator<ESTileFireComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var fire, out var xform))
                {
                    if (xform.GridUid == ground && (int) MathF.Floor(xform.LocalPosition.X) == x)
                        return (uid, fire);
                }

                throw new AssertionException($"No fire at x={x}.");
            }

            Assert.That(groundFires.CanSpread(At(0)), Is.True, "The first fire starts a line.");
            Assert.That(groundFires.CanSpread(At(2)), Is.False, "A fire two tiles off joins that line.");
            Assert.That(entMan.GetComponent<WGGroundFireComponent>(At(2)).Budget, Is.Zero);
            Assert.That(groundFires.CanSpread(At(10)), Is.True, "A fire ten tiles off starts a line of its own.");
        });

        await PutOut(pair, ground);
        await pair.CleanReturnAsync();
    }

    /// <summary>Planet ground refuses new fires once it has its fill burning, however they are lit.</summary>
    [Test]
    public async Task APlanetGroundHoldsOnlySoManyFires()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var fires = server.System<ESTileFireSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableFeature(pair);
        var ground = (await BuildStandalone(pair))[0];

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var grass = new Tile(tileDefs[Grass].TileId);
            maps.SetTiles(ground, grid, Area(new Vector2i(-10, -10), new Vector2i(10, 10)).Select(i => (i, grass)).ToList());
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            var lit = Area(new Vector2i(-10, -10), new Vector2i(10, 10))
                .Count(i => fires.TryDoTileFire(new EntityCoordinates(ground, i.X + 0.5f, i.Y + 0.5f)));
            Assert.That(lit, Is.EqualTo(WGGroundFireSystem.MaxFiresPerGround),
                "Every fire past the planet's limit must be refused, even when lit in one tick.");
        });

        await PutOut(pair, ground);
        await pair.CleanReturnAsync();
    }

    /// <summary>Deletes the fires left on a ground, so the pooled server hands the next test none.</summary>
    private static async Task PutOut(TestPair pair, EntityUid ground)
    {
        var entMan = pair.Server.EntMan;
        await pair.Server.WaitPost(() =>
        {
            var lit = new List<EntityUid>();
            var query = entMan.EntityQueryEnumerator<ESTileFireComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == ground)
                    lit.Add(uid);
            }

            foreach (var uid in lit)
                entMan.DeleteEntity(uid);
        });
    }

    /// <summary>The smoke, missing oxygen and heat a ground fire leaves in a tracked tile go when it ends.</summary>
    [Test]
    public async Task BurntAirIsRestored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var fires = server.System<ESTileFireSystem>();
        var atmos = server.System<AtmosphereSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();

        await EnableFeature(pair);
        var ground = (await BuildStandalone(pair))[0];
        var spot = new Vector2i(2, 2);
        var fire = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var grid = entMan.GetComponent<MapGridComponent>(ground);
            var wood = new Tile(tileDefs[Wood].TileId);
            maps.SetTiles(ground, grid, Area(new Vector2i(0, 0), new Vector2i(4, 4)).Select(i => (i, wood)).ToList());
        });

        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitPost(() =>
        {
            Assert.That(fires.TryDoTileFire(new EntityCoordinates(ground, 2.5f, 2.5f)), Is.True);
            fire = entMan.EntityQuery<ESTileFireComponent>()
                .First(f => entMan.GetComponent<TransformComponent>(f.Owner).GridUid == ground).Owner;
        });

        await server.WaitRunTicks(pair.SecondsToTicks(4f));

        float normalOxygen = 0f;

        await server.WaitPost(() =>
        {
            var air = atmos.GetTileMixture(ground, ground, spot)!;
            Assert.That(air.Immutable, Is.False, "The built tile has no air of its own.");
            var map = atmos.GetTileMixture(null, ground, Vector2i.Zero)!;
            normalOxygen = map.GetMoles(Gas.Oxygen) * air.Volume / map.Volume;

            air.SetMoles(Gas.Oxygen, 0.1f);
            air.SetMoles(Gas.Smoke, 5f);
            air.Temperature = 900f;
            entMan.DeleteEntity(fire);
        });

        await server.WaitAssertion(() =>
        {
            var air = atmos.GetTileMixture(ground, ground, spot)!;
            Assert.That(air.GetMoles(Gas.Oxygen), Is.GreaterThanOrEqualTo(normalOxygen - 0.01f), "The burned oxygen was not put back.");
            Assert.That(air.GetMoles(Gas.Smoke), Is.Zero, "The smoke stayed.");
            Assert.That(air.Temperature, Is.LessThan(400f), "The heat stayed.");
        });

        await pair.CleanReturnAsync();
    }
}
