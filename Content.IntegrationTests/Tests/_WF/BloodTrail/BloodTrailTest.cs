#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.BloodTrail;
using Content.Server.Body.Systems;
using Content.Server.Decals;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Gravity;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Timing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WF.BloodTrail;

/// <summary>
/// Touching a puddle stains a mob; a stained mob leaves footprints while walking and drag marks while down.
/// </summary>
[TestFixture]
[TestOf(typeof(FootPrintsSystem))]
public sealed class BloodTrailTest
{
    private const string Human = "MobHuman";
    private const int Row = 12;
    private const string Paint = "Dirt";
    private const string Mop = "MopItem";
    private const string Water = "Water";
    private static readonly Box2 Floor = new(-1, -1, Row + 1, 2);

    public enum Posture
    {
        Walking,
        Crawling,
        Critical,
    }

    [TestCase("Blood", false, true)]
    [TestCase("Blood", true, false)]
    [TestCase("Water", false, false)]
    public async Task PuddleContactStains(string reagent, bool airborne, bool stained)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();

        var human = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            human = SpawnOnFloor(entMan, map);
            entMan.System<SharedPhysicsSystem>().SetBodyStatus(human,
                entMan.GetComponent<PhysicsComponent>(human),
                airborne ? BodyStatus.InAir : BodyStatus.OnGround);

            // Two units is under the slip threshold: staining must not depend on slipping.
            Assert.That(entMan.System<PuddleSystem>().TrySpillAt(new EntityCoordinates(map.Grid, 2.5f, 0.5f),
                new Solution(reagent, 2), out _, sound: false), Is.True);
        });

        await pair.RunTicksSync(2);
        await server.WaitPost(() => xformSys.SetLocalPosition(human, new Vector2(2.5f, 0.5f)));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var alpha = entMan.TryGetComponent(human, out FootPrintsComponent? prints) ? prints.PrintsColor.A : 0f;
            Assert.That(alpha > 0f, Is.EqualTo(stained));
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(Posture.Walking)]
    [TestCase(Posture.Crawling)]
    [TestCase(Posture.Critical)]
    public async Task StainedMobLeavesTrail(Posture posture)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();
        var decals = entMan.System<DecalSystem>();
        var footPrints = entMan.System<FootPrintsSystem>();

        var human = EntityUid.Invalid;
        var steps = 0;
        await server.WaitAssertion(() =>
        {
            human = SpawnOnFloor(entMan, map);

            switch (posture)
            {
                case Posture.Crawling:
                    Assert.That(entMan.System<StandingStateSystem>().Down(human, playSound: false), Is.True);
                    break;
                case Posture.Critical:
                    entMan.System<MobStateSystem>().ChangeMobState(human, MobState.Critical);
                    break;
            }

            footPrints.Stain(human, Color.Red);
            var prints = entMan.GetComponent<FootPrintsComponent>(human);
            steps = (int) MathF.Ceiling(prints.PrintsColor.A / prints.ColorReduceAlpha);

            // More steps than the stain lasts, each longer than a stride.
            for (var i = 1; i <= steps + 3; i++)
                xformSys.SetLocalPosition(human, new Vector2(0.5f + i * 0.75f, 0.5f));
        });

        await server.WaitAssertion(() =>
        {
            var prints = entMan.GetComponent<FootPrintsComponent>(human);
            var placed = decals.GetDecalsIntersecting(map.Grid, Floor).Select(entry => entry.Decal).ToList();
            var dragMarks = placed.Count(decal => prints.DraggingDecals.Contains(decal.Id));
            var footsteps = placed.Count(decal =>
                decal.Id == prints.LeftBareDecal.Id || decal.Id == prints.RightBareDecal.Id);

            Assert.Multiple(() =>
            {
                Assert.That(prints.PrintsColor.A, Is.Zero, "The stain should have run out.");
                Assert.That(placed.All(decal => decal.Cleanable), Is.True);

                if (posture == Posture.Walking)
                {
                    Assert.That(footsteps, Is.GreaterThan(1));
                    Assert.That(dragMarks, Is.Zero);
                }
                else
                {
                    Assert.That(dragMarks, Is.GreaterThan(1));
                    Assert.That(footsteps, Is.Zero);
                }

                Assert.That(placed, Has.Count.LessThanOrEqualTo(steps));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A bleeding body is stained by the blood it spills, so crawling with a wound leaves a trail.</summary>
    [Test]
    public async Task BleedingCrawlerLeavesTrail()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();
        var bloodstream = entMan.System<BloodstreamSystem>();
        var decals = entMan.System<DecalSystem>();

        var human = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            human = SpawnOnFloor(entMan, map);
            Assert.That(entMan.System<StandingStateSystem>().Down(human, playSound: false), Is.True);
        });

        for (var i = 1; i <= 8; i++)
        {
            var x = 0.5f + i * 0.5f;
            await server.WaitPost(() =>
            {
                // What a bleed does every interval: blood leaves the body and spills under it.
                bloodstream.TryModifyBloodLevel(human, -2);
                xformSys.SetLocalPosition(human, new Vector2(x, 0.5f));
            });
            await pair.RunTicksSync(3);
        }

        await server.WaitAssertion(() =>
        {
            var prints = entMan.GetComponent<FootPrintsComponent>(human);
            var dragMarks = decals.GetDecalsIntersecting(map.Grid, Floor)
                .Count(entry => prints.DraggingDecals.Contains(entry.Decal.Id));
            Assert.That(dragMarks, Is.GreaterThan(1));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Space cleaner and a mop's water both take the prints off, and water leaves other decals alone.</summary>
    [TestCase("SpaceCleaner", false)]
    [TestCase("Water", true)]
    public async Task PrintsWashOff(string reagent, bool paintStays)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mapSys = entMan.System<SharedMapSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var decals = entMan.System<DecalSystem>();

        await server.WaitAssertion(() =>
        {
            var human = SpawnOnFloor(entMan, map);
            entMan.System<FootPrintsSystem>().Stain(human, Color.Red);
            for (var i = 1; i <= 4; i++)
                xformSys.SetLocalPosition(human, new Vector2(0.5f + i * 0.75f, 0.5f));

            entMan.System<StandingStateSystem>().Down(human, playSound: false);
            for (var i = 5; i <= 8; i++)
                xformSys.SetLocalPosition(human, new Vector2(0.5f + i * 0.75f, 0.5f));

            Assert.That(decals.TryAddDecal(Paint, new EntityCoordinates(map.Grid, 2, 0), out _, cleanable: true), Is.True);

            List<string> Placed() => decals.GetDecalsIntersecting(map.Grid, Floor).Select(entry => entry.Decal.Id).ToList();
            var before = Placed();
            Assert.That(before.Count(id => id.StartsWith(FootPrintsSystem.DecalPrefix)), Is.EqualTo(8));
            Assert.That(before.Any(id => id.Contains("Dragging")), Is.True);

            var proto = server.ProtoMan.Index<ReagentPrototype>(reagent);
            var grid = entMan.GetComponent<MapGridComponent>(map.Grid);
            for (var x = 0; x < Row; x++)
                proto.ReactionTile(mapSys.GetTileRef(map.Grid, grid, new Vector2i(x, 0)), FixedPoint2.New(5), entMan, null);

            var after = Placed();
            Assert.Multiple(() =>
            {
                Assert.That(after.Where(id => id.StartsWith(FootPrintsSystem.DecalPrefix)), Is.Empty);
                Assert.That(after.Contains(Paint), Is.EqualTo(paintStays));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The prints lie where there is no puddle to mop, so a wet mop used on the bare floor takes them off, along
    /// with any other cleanable decal. A dry one does not.
    /// </summary>
    [Test]
    public async Task MopWashesPrintsOffBareFloor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();
        var decals = entMan.System<DecalSystem>();
        var interaction = entMan.System<SharedInteractionSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var human = SpawnOnFloor(entMan, map);
            entMan.System<FootPrintsSystem>().Stain(human, Color.Red);
            for (var i = 1; i <= 6; i++)
                xformSys.SetLocalPosition(human, new Vector2(0.5f + i * 0.75f, 0.5f));

            Assert.That(decals.TryAddDecal(Paint, new EntityCoordinates(map.Grid, 2, 0), out _, cleanable: true), Is.True);

            var mop = entMan.SpawnEntity(Mop, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            entMan.RemoveComponent<UseDelayComponent>(mop);
            Assert.That(solutions.TryGetSolution(mop, AbsorbentComponent.SolutionName, out var soln, out var solution), Is.True);
            Assert.That(solution!.Volume, Is.EqualTo(FixedPoint2.Zero), "the mop no longer starts dry.");

            List<string> Placed() => decals.GetDecalsIntersecting(map.Grid, Floor).Select(entry => entry.Decal.Id).ToList();
            int Prints() => Placed().Count(id => id.StartsWith(FootPrintsSystem.DecalPrefix));

            void MopRow()
            {
                for (var x = 0; x < Row; x++)
                    interaction.InteractDoAfter(human, mop, null, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f), true);
            }

            Assert.That(Prints(), Is.EqualTo(6));
            MopRow();
            Assert.That(Prints(), Is.EqualTo(6), "A dry mop washed the floor.");

            Assert.That(solutions.TryAddReagent(soln!.Value, Water, 20), Is.True);
            MopRow();
            Assert.Multiple(() =>
            {
                Assert.That(Prints(), Is.Zero);
                Assert.That(Placed(), Does.Not.Contain(Paint));
                Assert.That(solution.Volume, Is.LessThan(FixedPoint2.New(20)).And.GreaterThan(FixedPoint2.New(15)),
                    "Washing should cost the mop a little water.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Mopping a small blood puddle on a printed tile: the floor drinks all the mop's water, and the emptied puddle
    /// must still go rather than stay as an unmoppable 0u puddle.
    /// </summary>
    [Test]
    public async Task MopEmptiesPuddleOnPrintedTile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var decals = entMan.System<DecalSystem>();
        var interaction = entMan.System<SharedInteractionSystem>();
        var solutions = entMan.System<SharedSolutionContainerSystem>();

        var puddle = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            var human = SpawnOnFloor(entMan, map);
            var print = new FootPrintsComponent().LeftBareDecal;
            for (var i = 0; i < 4; i++)
                Assert.That(decals.TryAddDecal(print,new EntityCoordinates(map.Grid, 2.1f + i * 0.1f, 0.1f), out _, cleanable: true), Is.True);

            Assert.That(entMan.System<PuddleSystem>().TrySpillAt(new EntityCoordinates(map.Grid, 2.5f, 0.5f),
                new Solution("Blood", FixedPoint2.New(0.5f)), out puddle, sound: false), Is.True);

            var mop = entMan.SpawnEntity(Mop, new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            entMan.RemoveComponent<UseDelayComponent>(mop);
            Assert.That(solutions.TryGetSolution(mop, AbsorbentComponent.SolutionName, out var soln, out _), Is.True);
            Assert.That(solutions.TryAddReagent(soln!.Value, Water, 20), Is.True);

            interaction.InteractDoAfter(human, mop, puddle, entMan.GetComponent<TransformComponent>(puddle).Coordinates, true);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(entMan.Deleted(puddle), Is.True, "The emptied puddle stayed."));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PrintsStopPilingUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var xformSys = entMan.System<SharedTransformSystem>();
        var decals = entMan.System<DecalSystem>();

        await server.WaitAssertion(() =>
        {
            var human = SpawnOnFloor(entMan, map);
            entMan.System<FootPrintsSystem>().Stain(human, Color.Red);
            var prints = entMan.GetComponent<FootPrintsComponent>(human);
            prints.ColorReduceAlpha = 0f;

            // Pace back and forth inside one tile.
            for (var i = 0; i < FootPrintsSystem.MaxPrintsNearby * 4; i++)
                xformSys.SetLocalPosition(human, new Vector2(i % 2 == 0 ? 1.1f : 1.9f, 0.5f));

            Assert.That(decals.GetDecalsIntersecting(map.Grid, Floor), Has.Count.InRange(FootPrintsSystem.MaxPrintsNearby,
                FootPrintsSystem.MaxPrintsNearby * 2));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Lays a row of floor with gravity and spawns a human at its start.</summary>
    private static EntityUid SpawnOnFloor(IEntityManager entMan, TestMapData map)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        for (var x = 0; x < Row; x++)
            mapSys.SetTile(map.Grid, new Vector2i(x, 0), map.Tile.Tile);

        entMan.EnsureComponent<GravityComponent>(map.Grid).Enabled = true;
        return entMan.SpawnEntity(Human, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
    }
}
