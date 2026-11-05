#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Structures;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Structures;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

using static Content.IntegrationTests.Tests._WF.Remade.WFRemadeDirectorTest;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The corruption growths: nodes, cysts, snares, maws, nests, harvesters and eyes, and the Director building them.</summary>
[TestFixture]
public sealed class StructuresTest
{
    [TestPrototypes]
    private const string Prototypes = @"
# The stock snare only trips a running, weighty mob; the test map has no gravity and nobody runs, so lift both gates.
- type: entity
  id: WFTestSnare
  parent: WFRemadeSnare
  components:
  - type: StepTrigger
    stepOn: true
    intersectRatio: 0.2
    requiredTriggeredSpeed: 0
    ignoreWeightless: true
";

    private static EntityUid SpawnScion(IEntityManager entMan, EntityCoordinates at, float resonance)
    {
        var scion = entMan.SpawnEntity("WFMonolithScion", at);
        entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
        entMan.GetComponent<WFMonolithComponent>(scion).Resonance = resonance;
        return scion;
    }

    /// <summary>Corruption with no Monolith behind it: a tile growths can stand on that never spreads or decays.</summary>
    private static void Corrupt(IEntityManager entMan, EntityCoordinates at)
    {
        entMan.SpawnEntity("WFCorruption", at);
    }

    /// <summary>Stops the Director planning anything of its own so a test can drive one behaviour at a time.</summary>
    private static WFRemadeDirectorComponent Quiesce(IEntityManager entMan, EntityUid scion, IGameTiming timing, bool building = false)
    {
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        var far = timing.CurTime + TimeSpan.FromHours(1);
        director.Phase = WFRemadeDirectorPhase.Quiet;
        director.PhaseEnd = far;
        director.NextAmbush = far;
        director.NextStalker = far;
        director.NextDread = far;
        director.NextBuild = building ? TimeSpan.Zero : far;
        return director;
    }

    private static float FarthestTile(IEntityManager entMan, SharedTransformSystem transforms, EntityUid scion, WFMonolithComponent monolith, out EntityUid tile)
    {
        var origin = transforms.GetWorldPosition(scion);
        var best = 0f;
        tile = default;
        foreach (var uid in monolith.Corruption)
        {
            var distance = (transforms.GetWorldPosition(uid) - origin).Length();
            if (distance > best)
            {
                best = distance;
                tile = uid;
            }
        }

        return best;
    }

    private static float Slash(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.TryGetValue("Slash", out var slash) ? slash.Float() : 0f;
    }

    [Test]
    public async Task ANodeReachesPastTheMonolithAndStarvesWhenKilled()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var growths = entMan.System<WFGrowthSystem>();
        var limit = 0;
        var hull = 0;
        EntityUid scion = default, node = default;
        var reach = 0f;

        await server.WaitPost(() =>
        {
            // A small deck, so a Growth Monolith's share of it is quick to fill.
            CorruptionTest.LayDeck(pair, map, 8);
            hull = entMan.System<WFCorruptionSystem>().HullTiles(map.Grid.Owner);
            limit = WFCorruptionSystem.Limit(WFMonolithStage.Growth, hull);
            scion = SpawnScion(entMan, At(map, 0, 0), 40f);
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        await RunUntil(pair, () => monolith.Corruption.Count >= limit, 240);

        await server.WaitPost(() =>
        {
            Assert.That(monolith.Corruption.Count, Is.EqualTo(limit), "the Monolith didn't fill its own cap.");
            reach = FarthestTile(entMan, transforms, scion, monolith, out var edge);
            var def = server.ProtoMan.Index<WFRemadeGrowthPrototype>("WFCorruptionPropagator");
            node = growths.TryBuild(def, entMan.GetComponent<TransformComponent>(edge).Coordinates, scion, ready: true, pay: false) ?? default;
            Assert.That(node, Is.Not.EqualTo(default(EntityUid)), "the propagator wouldn't grow on the edge of the corruption.");
        });

        var nodeComp = entMan.GetComponent<WFCorruptionNodeComponent>(node);
        // The corruption spreads at random, so on this small deck a node grown at its far edge may have fewer free
        // tiles in reach than it could feed; it fills those.
        var feedable = 0;
        await server.WaitPost(() =>
        {
            var at = entMan.GetComponent<TransformComponent>(node).LocalPosition;
            var taken = monolith.Corruption
                .Select(c => entMan.GetComponent<TransformComponent>(c).LocalPosition)
                .Select(p => new Vector2i((int) MathF.Floor(p.X), (int) MathF.Floor(p.Y)))
                .ToHashSet();
            var free = 0;
            for (var x = -8; x < 8; x++)
            for (var y = -8; y < 8; y++)
            {
                if (!taken.Contains(new Vector2i(x, y)) && (new Vector2(x + 0.5f, y + 0.5f) - at).Length() <= nodeComp.Reach - 0.5f)
                    free++;
            }

            feedable = Math.Min(nodeComp.Capacity, free);
            Assert.That(feedable, Is.GreaterThan(limit / 2), "the node had too little room to show it feeds past the Monolith.");
        });

        await RunUntil(pair, () => nodeComp.Tiles.Count >= feedable, 900);

        await server.WaitPost(() =>
        {
            Assert.That(nodeComp.Tiles.Count, Is.GreaterThanOrEqualTo(feedable), "the node never filled what it could feed.");
            Assert.That(nodeComp.Tiles.Count, Is.LessThanOrEqualTo(nodeComp.Capacity));
            Assert.That(monolith.Corruption.Count, Is.EqualTo(limit + nodeComp.Tiles.Count), "the node's tiles didn't raise the cap.");
            Assert.That(monolith.Corruption.Count, Is.LessThanOrEqualTo(WFCorruptionSystem.Cap(WFMonolithStage.Growth, hull, nodeComp.Capacity)));
            Assert.That(entMan.System<WFCorruptionSystem>().GridTiles(map.Grid.Owner), Is.EqualTo(monolith.Corruption.Count), "the hull's tile count drifted from the real count.");
            Assert.That(FarthestTile(entMan, transforms, scion, monolith, out _), Is.GreaterThan(reach + 1.4f), "the corruption didn't get past the Monolith's reach.");
            entMan.DeleteEntity(node);
        });

        await RunUntil(pair, () => monolith.Corruption.Count <= limit, 90);
        await server.WaitAssertion(() =>
        {
            Assert.That(monolith.Corruption.Count, Is.EqualTo(limit), "the tiles the node fed didn't starve.");
            Assert.That(entMan.System<WFCorruptionSystem>().GridTiles(map.Grid.Owner), Is.EqualTo(limit));
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACorruptionHullNeverPassesItsCap()
    {
        Assert.That(WFCorruptionSystem.Cap(WFMonolithStage.Crescendo, 4000, 100000), Is.EqualTo(WFCorruptionSystem.HullCap));
        Assert.That(WFCorruptionSystem.Cap(WFMonolithStage.Growth, 2000, 0), Is.EqualTo(WFCorruptionSystem.Limit(WFMonolithStage.Growth, 2000)));
        Assert.That(WFCorruptionSystem.Limit(WFMonolithStage.Rapture, 2000), Is.EqualTo(2000), "Rapture should reach the whole hull.");
        Assert.That(WFCorruptionSystem.Limit(WFMonolithStage.Harvest, 2000), Is.InRange(1000, 1200), "Harvest should cover over half.");
        Assert.That(WFCorruptionSystem.Limit(WFMonolithStage.Growth, 2000), Is.InRange(400, 600), "Growth should cover about a quarter.");
        Assert.That(WFCorruptionSystem.Limit(WFMonolithStage.Growth, 100), Is.EqualTo(64), "a small hull keeps its old allowance.");
        Assert.That(WFCorruptionSystem.Limit(WFMonolithStage.Hymn, 2000), Is.Zero);
        await Task.CompletedTask;
    }

    [Test]
    public async Task ASnareTripsAWalkerOncePerReload()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid snare = default, walker = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            SpawnScion(entMan, At(map, -20, -20), 40f);
            Corrupt(entMan, At(map, 5, 5));
            snare = entMan.SpawnEntity("WFTestSnare", At(map, 5, 5));
            walker = entMan.SpawnEntity("MobHuman", At(map, 5, 5));
        });

        var comp = entMan.GetComponent<WFSnareComponent>(snare);
        await RunUntil(pair, () => comp.Trips > 0, 8);
        await server.WaitAssertion(() =>
        {
            Assert.That(comp.Trips, Is.EqualTo(1), "the snare didn't trip the walker.");
            Assert.That(entMan.HasComponent<KnockedDownComponent>(walker), Is.True, "the walker wasn't knocked down.");
            transforms.SetCoordinates(walker, At(map, 9, 9));
        });

        await pair.RunSeconds(1.5f);
        await server.WaitPost(() => transforms.SetCoordinates(walker, At(map, 5, 5)));
        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(comp.Trips, Is.EqualTo(1), "the snare tripped twice inside its reload.");
            transforms.SetCoordinates(walker, At(map, 9, 9));
        });

        await pair.RunSeconds(10f);
        await server.WaitPost(() => transforms.SetCoordinates(walker, At(map, 5, 5)));
        await RunUntil(pair, () => comp.Trips > 1, 8);
        await server.WaitAssertion(() => Assert.That(comp.Trips, Is.EqualTo(2), "the snare never reloaded."));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AMawWarnsThenGrabsThenBitesAndCanBePulledFree()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var buckle = entMan.System<SharedBuckleSystem>();
        EntityUid scion = default, maw = default, victim = default, friend = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            Corrupt(entMan, At(map, 6, 6));
            maw = entMan.SpawnEntity("WFRemadeMaw", At(map, 6, 6));
            victim = entMan.SpawnEntity("MobHuman", At(map, 6, 6));
            friend = entMan.SpawnEntity("MobHuman", At(map, 7, 6));
        });

        var comp = entMan.GetComponent<WFMawComponent>(maw);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        TimeSpan grabAt = default;
        await RunUntil(pair, () => comp.Pending.ContainsKey(victim), 5);
        await server.WaitAssertion(() =>
        {
            Assert.That(comp.Pending.ContainsKey(victim), Is.True, "the maw gave no warning.");
            grabAt = comp.Pending[victim];
            Assert.That((grabAt - timing.CurTime).TotalSeconds, Is.GreaterThan(2.0), "the warning was too short.");
            Assert.That(entMan.GetComponent<BuckleComponent>(victim).Buckled, Is.False, "it grabbed with no warning.");
        });

        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() => Assert.That(entMan.GetComponent<BuckleComponent>(victim).Buckled, Is.False, "it grabbed before the 3 seconds were up."));

        await RunUntil(pair, () => entMan.GetComponent<BuckleComponent>(victim).Buckled, 6);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BuckleComponent>(victim).Buckled, Is.True, "the maw never grabbed.");
            Assert.That(timing.CurTime, Is.GreaterThanOrEqualTo(grabAt), "it grabbed before its warning ran out.");
            Assert.That(comp.Grabs, Is.EqualTo(1));
            Assert.That(entMan.HasComponent<WFMawHeldComponent>(victim), Is.True);
            comp.PullTime = TimeSpan.FromSeconds(2);
            comp.NextBite = timing.CurTime + TimeSpan.FromHours(1);
        });

        // A fresh buckle can't be undone for a moment, so wait that out. Then nobody just clicks free of it: the click starts a pull.
        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(buckle.TryUnbuckle(victim, friend, popup: true), Is.False, "a click freed them at once.");
            Assert.That(entMan.GetComponent<BuckleComponent>(victim).Buckled, Is.True);
        });

        await RunUntil(pair, () => !entMan.GetComponent<BuckleComponent>(victim).Buckled, 10);
        await server.WaitAssertion(() =>
        {
            var doAfters = entMan.TryGetComponent<DoAfterComponent>(friend, out var doAfter) ? doAfter.DoAfters.Count : -1;
            Assert.That(entMan.GetComponent<BuckleComponent>(victim).Buckled, Is.False,
                $"their friend couldn't pull them free. friend's doafters={doAfters} friend state={entMan.GetComponent<MobStateComponent>(friend).CurrentState}");
            Assert.That(entMan.HasComponent<WFMawHeldComponent>(victim), Is.False, "the held mark stayed on a freed victim.");
            Assert.That(comp.Immune.ContainsKey(victim), Is.True);
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(comp.Grabs, Is.EqualTo(1), "it grabbed them straight back.");
            Assert.That(director.Biomass, Is.Zero, "it paid for a meal it didn't have.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KillingAMawLetsGoOfWhoItHolds()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default, maw = default, victim = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            Corrupt(entMan, At(map, 6, 6));
            maw = entMan.SpawnEntity("WFRemadeMaw", At(map, 6, 6));
            victim = entMan.SpawnEntity("MobHuman", At(map, 6, 6));
            entMan.GetComponent<WFMawComponent>(maw).BiteEvery = TimeSpan.FromHours(1);
        });

        await RunUntil(pair, () => entMan.GetComponent<BuckleComponent>(victim).Buckled, 10);
        await server.WaitPost(() =>
        {
            Assert.That(entMan.GetComponent<BuckleComponent>(victim).Buckled, Is.True, "the maw never grabbed.");
            var blow = new DamageSpecifier();
            blow.DamageDict["Blunt"] = 400;
            entMan.System<DamageableSystem>().TryChangeDamage(maw, blow);
        });

        await RunUntil(pair, () => entMan.Deleted(maw), 5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(maw), Is.True, "a crew's blows couldn't kill the maw.");
            Assert.That(entMan.GetComponent<BuckleComponent>(victim).Buckled, Is.False, "killing the maw left its victim held.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AMawBitesWhoItHoldsTakesLimbsAndPaysTheDirector()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var body = entMan.System<SharedBodySystem>();
        EntityUid scion = default, maw = default, victim = default;

        int Limbs(EntityUid uid) =>
            body.GetBodyChildrenOfType(uid, BodyPartType.Arm).Count() + body.GetBodyChildrenOfType(uid, BodyPartType.Leg).Count();

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            Corrupt(entMan, At(map, 6, 6));
            maw = entMan.SpawnEntity("WFRemadeMaw", At(map, 6, 6));
            victim = entMan.SpawnEntity("MobHuman", At(map, 6, 6));
        });

        var comp = entMan.GetComponent<WFMawComponent>(maw);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        var before = 0;
        await RunUntil(pair, () => comp.Grabs > 0, 8);
        await server.WaitPost(() =>
        {
            Assert.That(comp.Grabs, Is.EqualTo(1));
            before = Limbs(victim);
            Assert.That(before, Is.GreaterThan(0), "the test body has no limbs.");
            comp.BiteEvery = TimeSpan.FromSeconds(0.5);
            comp.LimbEvery = 3;
        });

        await RunUntil(pair, () => Slash(entMan, victim) > 0f, 6);
        await server.WaitAssertion(() => Assert.That(Slash(entMan, victim), Is.GreaterThan(0f), "it didn't hurt who it held."));

        await RunUntil(pair, () => Limbs(victim) < before || entMan.Deleted(victim), 20);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(victim) || Limbs(victim) < before, Is.True, "it took no limb.");
            Assert.That(director.Biomass, Is.GreaterThan(0f), "what it ate paid the Director nothing.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AMawCannotGrabWhoSteppedOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid scion = default, maw = default, victim = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            Corrupt(entMan, At(map, 6, 6));
            maw = entMan.SpawnEntity("WFRemadeMaw", At(map, 6, 6));
            victim = entMan.SpawnEntity("MobHuman", At(map, 6, 6));
        });

        var comp = entMan.GetComponent<WFMawComponent>(maw);
        await RunUntil(pair, () => comp.Pending.ContainsKey(victim), 5);
        await server.WaitPost(() =>
        {
            Assert.That(comp.Pending.ContainsKey(victim), Is.True, "the maw gave no warning.");
            transforms.SetCoordinates(victim, At(map, 3, 6));
        });

        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BuckleComponent>(victim).Buckled, Is.False, "it grabbed someone who had stepped off.");
            Assert.That(comp.Grabs, Is.Zero);
            Assert.That(comp.Pending, Is.Empty);
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AMawEatsADeadBodyLyingOnIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default, maw = default, corpse = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            Corrupt(entMan, At(map, 6, 6));
            maw = entMan.SpawnEntity("WFRemadeMaw", At(map, 6, 6));
            corpse = entMan.SpawnEntity("MobHuman", At(map, 6, 6));
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            entMan.GetComponent<WFMawComponent>(maw).BiteEvery = TimeSpan.FromSeconds(0.5);
        });

        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await RunUntil(pair, () => entMan.Deleted(corpse), 30);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(corpse), Is.True, "the maw left the body alone.");
            Assert.That(director.Biomass, Is.GreaterThanOrEqualTo(entMan.GetComponent<WFMawComponent>(maw).CorpseBiomass), "the meal paid too little.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACystFiresAtAWalkerInItsArcAndNotBesideIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var growths = entMan.System<WFGrowthSystem>();
        EntityUid scion = default, front = default, side = default, hit = default;
        EntityUid? cystA = null, cystB = null;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            var def = server.ProtoMan.Index<WFRemadeGrowthPrototype>("WFRemadeCyst");

            // Each hangs on a wall to its east, so it faces west.
            foreach (var y in new[] { 0, -8 })
            {
                Corrupt(entMan, At(map, 9, y));
                entMan.SpawnEntity("WallSolid", At(map, 10, y));
            }

            cystA = growths.TryBuild(def, At(map, 9, 0), scion, ready: true, pay: false);
            cystB = growths.TryBuild(def, At(map, 9, -8), scion, ready: true, pay: false);
            Assert.That(cystA, Is.Not.Null, "the cyst wouldn't hang on a wall.");
            Assert.That(cystB, Is.Not.Null);
            front = entMan.SpawnEntity("MobCarp", At(map, 6, 0));
            side = entMan.SpawnEntity("MobCarp", At(map, 9, -5));
            hit = front;
        });

        var a = entMan.GetComponent<WFCystComponent>(cystA!.Value);
        var b = entMan.GetComponent<WFCystComponent>(cystB!.Value);
        // Mobs keep drifting in the test map's weightlessness, so hold the walkers where they stand.
        var physics = entMan.System<SharedPhysicsSystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        bool Pinned(Func<bool> done)
        {
            transforms.SetCoordinates(front, At(map, 6, 0));
            transforms.SetCoordinates(side, At(map, 9, -5));
            physics.SetLinearVelocity(front, Vector2.Zero);
            physics.SetLinearVelocity(side, Vector2.Zero);
            return done();
        }

        await RunUntil(pair, () => Pinned(() => a.Shots > 0), 25);
        await RunUntil(pair, () => Pinned(() => false), 2);
        await server.WaitAssertion(() =>
        {
            var growth = entMan.GetComponent<WFGrowthComponent>(cystA!.Value);
            var diag = $"ready={a.Ready} reloadAt={a.ReloadAt} now={server.ResolveDependency<IGameTiming>().CurTime} active={growth.Active} dormant={growth.Dormant} "
                       + $"facing={entMan.System<SharedTransformSystem>().GetWorldRotation(cystA.Value).ToWorldVec()} pos={entMan.System<SharedTransformSystem>().GetWorldPosition(cystA.Value)} "
                       + $"walker={entMan.System<SharedTransformSystem>().GetWorldPosition(front)}";
            Assert.That(a.Shots, Is.GreaterThan(0), "the cyst didn't fire at a walker in front of it. " + diag);
            Assert.That(b.Ready, Is.True, "the second cyst never loaded, so the test proves nothing.");
            Assert.That(b.Shots, Is.Zero, "the cyst fired at a walker beside it, outside its arc.");
            Assert.That(entMan.GetComponent<DamageableComponent>(hit).TotalDamage.Float(), Is.GreaterThan(0f), "the bomb never hit.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ANestMakesItsRemadeSlowlyAndStopsAtTwo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var growths = entMan.System<WFGrowthSystem>();
        var mobState = entMan.System<MobStateSystem>();
        EntityUid scion = default;
        EntityUid? nest = null;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).Biomass = 500f;
            Corrupt(entMan, At(map, 6, 6));
        });

        await pair.RunSeconds(2f);
        await server.WaitPost(() =>
        {
            var def = server.ProtoMan.Index<WFRemadeGrowthPrototype>("WFRemadeNest");
            nest = growths.TryBuild(def, At(map, 6, 6), scion, ready: true, pay: false);
            Assert.That(nest, Is.Not.Null, "the nest wouldn't grow on corruption.");
            var comp = entMan.GetComponent<WFNestComponent>(nest!.Value);
            comp.Produces = "WFRemadeSlasher";
            comp.Interval = TimeSpan.FromSeconds(3);
        });

        var nestComp = entMan.GetComponent<WFNestComponent>(nest!.Value);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await RunUntil(pair, () => nestComp.Made >= 2, 40);
        await pair.RunSeconds(14f);
        await server.WaitAssertion(() =>
        {
            Assert.That(nestComp.Made, Is.EqualTo(2), "the nest didn't stop at two.");
            Assert.That(nestComp.Alive.Count, Is.EqualTo(2));
            Assert.That(director.Horde.Count, Is.EqualTo(2), "what it made doesn't answer to the Director.");
            Assert.That(director.Biomass, Is.EqualTo(400f).Within(0.01f), "the Director wasn't charged for them.");
            foreach (var remade in nestComp.Alive)
            {
                Assert.That(entMan.GetComponent<WFRemadeComponent>(remade).Source, Is.EqualTo(scion));
            }

            mobState.ChangeMobState(nestComp.Alive.First(), MobState.Dead);
        });

        await RunUntil(pair, () => nestComp.Made >= 3, 20);
        await server.WaitAssertion(() =>
        {
            Assert.That(nestComp.Made, Is.EqualTo(3), "the nest didn't replace what died.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AHarvesterRaisesBiomassFromFoodAndTheDead()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default, harvester = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            Corrupt(entMan, At(map, 4, 4));
            harvester = entMan.SpawnEntity("WFRemadeHarvester", At(map, 4, 4));
            entMan.GetComponent<WFHarvesterComponent>(harvester).WarmUp = TimeSpan.FromSeconds(2);
            var corpse = entMan.SpawnEntity("MobHuman", At(map, 7, 4));
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            for (var i = 0; i < 4; i++)
                entMan.SpawnEntity("FoodBreadPlain", At(map, 5 + i * 0.3f, 7));
        });

        var comp = entMan.GetComponent<WFHarvesterComponent>(harvester);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(comp.Running, Is.False, "it worked before it warmed up.");
            Assert.That(director.Biomass, Is.Zero);
        });

        await RunUntil(pair, () => director.Biomass > 1f, 30);
        await server.WaitAssertion(() =>
        {
            Assert.That(comp.Running, Is.True, "it never started.");
            Assert.That(comp.Rate, Is.GreaterThan(0f));
            Assert.That(director.Biomass, Is.GreaterThan(1f), "it gave the Director nothing.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnEyeTellsTheDirectorWhereACrewmanWalked()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid scion = default, crewman = default, eye = default, remade = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, -15, 0));
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            Corrupt(entMan, At(map, 3, 0));
            eye = entMan.SpawnEntity("WFRemadeEye", At(map, 3, 0));
            remade = entMan.SpawnEntity("WFRemadeSlasher", At(map, 14, 0));
            entMan.System<WFRemadeDirectorSystem>().Enlist(remade, scion);
            var behaviour = entMan.GetComponent<WFRemadeBehaviourComponent>(remade);
            behaviour.NextDrift = TimeSpan.FromDays(1);
            Assert.That(behaviour.Known, Is.Null);
        });

        var comp = entMan.GetComponent<WFEyeComponent>(eye);
        var behaviour = entMan.GetComponent<WFRemadeBehaviourComponent>(remade);
        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(comp.Alerts, Is.Zero, "the eye saw somebody a long way off.");
            Assert.That(behaviour.Known, Is.Null);
            transforms.SetCoordinates(crewman, At(map, 0, 0));
        });

        await RunUntil(pair, () => comp.Alerts > 0 && behaviour.Known != null, 10);
        await server.WaitAssertion(() =>
        {
            Assert.That(comp.Alerts, Is.GreaterThan(0), "the eye never saw a crewman walk in.");
            Assert.That(behaviour.Known, Is.Not.Null, "the Director never learned where they were.");
            var known = transforms.ToMapCoordinates(behaviour.Known!.Value).Position;
            Assert.That((known - transforms.GetWorldPosition(crewman)).Length(), Is.LessThan(5f), "it learned the wrong place.");
            Assert.That(entMan.GetComponent<WFGrowthComponent>(eye).State, Is.EqualTo(WFGrowthState.Active), "the eye didn't open.");
            var facing = transforms.GetWorldRotation(eye).ToWorldVec();
            Assert.That(Vector2.Dot(facing, Vector2.Normalize(transforms.GetWorldPosition(crewman) - transforms.GetWorldPosition(eye))), Is.GreaterThan(0.9f), "the eye didn't turn to the crewman.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheDirectorBuildsAtHarvestWithCrewNearAndBiomassInHand()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, 21, 0));
            scion = SpawnScion(entMan, At(map, 0, 0), 60f);
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await RunUntil(pair, () => monolith.Corruption.Count >= 30, 120);
        await server.WaitPost(() =>
        {
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Harvest));
            director.Biomass = 400f;
            director.NextBuild = TimeSpan.Zero;
        });

        await RunUntil(pair, () => director.Growths.Count > 0, 30);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Growths, Is.Not.Empty, "the Director built nothing.");
            Assert.That(director.Built, Is.GreaterThan(0));
            Assert.That(director.Biomass, Is.LessThan(400f), "it built for free.");
            Assert.That(director.Biomass, Is.GreaterThanOrEqualTo(director.BuildReserve), "it spent what it needed for a wave.");
            var growth = entMan.GetComponent<WFGrowthComponent>(director.Growths.First());
            Assert.That(growth.ReadyAt, Is.Not.Null, "with no Infector it should have put down a seed.");
            Assert.That(entMan.HasComponent<WFCorruptionNodeComponent>(director.Growths.First()), Is.True, "it didn't start with a node.");
            Assert.That(growth.Source, Is.EqualTo(scion));
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnInfectorWalksToTheSiteAndRaisesTheGrowth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default, infector = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, 21, 21));
            scion = SpawnScion(entMan, At(map, 0, 0), 60f);
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            infector = entMan.SpawnEntity("WFRemadeInfector", At(map, 2, 2));
            entMan.System<WFRemadeDirectorSystem>().Enlist(infector, scion);
            entMan.GetComponent<WFRemadeBehaviourComponent>(infector).NextDrift = TimeSpan.FromDays(1);
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        var lowest = 400f;
        await RunUntil(pair, () => monolith.Corruption.Count >= 30, 120);
        await server.WaitPost(() =>
        {
            director.Biomass = 400f;
            director.NextBuild = TimeSpan.Zero;
        });

        await RunUntil(pair, () => { lowest = Math.Min(lowest, director.Biomass); return entMan.HasComponent<WFRemadeBuildJobComponent>(infector); }, 30);
        await server.WaitAssertion(() => Assert.That(entMan.HasComponent<WFRemadeBuildJobComponent>(infector), Is.True, "the Director never sent the Infector."));

        var builtBefore = 0;
        await server.WaitPost(() => builtBefore = director.Built);
        await RunUntil(pair, () => { lowest = Math.Min(lowest, director.Biomass); return director.Built > builtBefore && !entMan.HasComponent<WFRemadeBuildJobComponent>(infector); }, 120);
        await server.WaitAssertion(() =>
        {
            var diag = entMan.TryGetComponent<WFRemadeBuildJobComponent>(infector, out var job)
                ? $"job site={entMan.System<SharedTransformSystem>().ToMapCoordinates(job.Site).Position} workUntil={job.WorkUntil} giveUp={job.GiveUpAt} "
                  + $"infector {Describe(pair, infector)} mode={entMan.GetComponent<WFRemadeBehaviourComponent>(infector).Mode}"
                : "no job";
            Assert.That(director.Built, Is.GreaterThan(builtBefore), "the Infector never raised the growth. " + diag);
            Assert.That(entMan.HasComponent<WFRemadeBuildJobComponent>(infector), Is.False, "the job never ended. " + diag);
            Assert.That(director.Growths.Any(uid => entMan.GetComponent<WFGrowthComponent>(uid).ReadyAt == null), Is.True, "an Infector's work should leave a grown growth, not a seed.");
            // Income keeps coming in while it walks, so look for the dip rather than the end figure.
            Assert.That(lowest, Is.LessThan(400f), "the growth wasn't paid for.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GrowthsGoDormantAndDieWhenTheirMonolithGoes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var spawned = new List<EntityUid>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            var i = 0;
            foreach (var proto in new[] { "WFRemadeMaw", "WFRemadeSnare", "WFRemadeLamp", "WFRemadeEye", "WFCorruptionRoot" })
            {
                Corrupt(entMan, At(map, 4, 4 + i));
                spawned.Add(entMan.SpawnEntity(proto, At(map, 4, 4 + i)));
                i += 2;
            }
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            foreach (var uid in spawned)
            {
                var growth = entMan.GetComponent<WFGrowthComponent>(uid);
                Assert.That(growth.Source, Is.EqualTo(scion), $"{entMan.ToPrettyString(uid)} isn't bound to the Monolith.");
                Assert.That(growth.Active, Is.True, $"{entMan.ToPrettyString(uid)} isn't active on corruption.");
            }

            entMan.DeleteEntity(scion);
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            foreach (var uid in spawned.Where(uid => !entMan.Deleted(uid)))
            {
                Assert.That(entMan.GetComponent<WFGrowthComponent>(uid).Dormant, Is.True, $"{entMan.ToPrettyString(uid)} carried on without its Monolith.");
            }
        });

        await RunUntil(pair, () => spawned.All(uid => entMan.Deleted(uid)), 90);
        await server.WaitAssertion(() => Assert.That(spawned.All(uid => entMan.Deleted(uid)), Is.True, "growths outlived their Monolith."));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AGrowthOffCorruptionGoesDormant()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default, maw = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, -20, -20), 40f);
            maw = entMan.SpawnEntity("WFRemadeMaw", At(map, 4, 4));
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFGrowthComponent>(maw).Dormant, Is.True, "a maw with no corruption under it was awake.");
            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARemadePlayingDeadExaminesAsDeadAndAliveOnceHit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mobState = entMan.System<MobStateSystem>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        var examine = entMan.System<ExamineSystemShared>();
        EntityUid scion = default, crewman = default, remade = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, -18, -18));
            scion = SpawnScion(entMan, At(map, -20, 20), 0f);
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            remade = entMan.SpawnEntity("WFRemadeSlasher", At(map, 0, 0));
            directors.Enlist(remade, scion);

            // What a corpse's examine says about itself.
            var dead = Loc.GetString("comp-mind-examined-dead", ("ent", remade));
            string Said() => examine.GetExamineText(remade, crewman).ToMarkup();

            Assert.That(Said(), Does.Not.Contain(dead), "a standing Remade examined as dead.");
            directors.PlayDead(remade);
            Assert.That(Said(), Does.Contain(dead), "a Remade playing dead still examined as alive.");
            Assert.That(mobState.IsAlive(remade), Is.True, "playing dead changed its real mob state.");

            var blow = new DamageSpecifier();
            blow.DamageDict["Slash"] = 10;
            entMan.System<DamageableSystem>().TryChangeDamage(remade, blow);
            Assert.That(Said(), Does.Not.Contain(dead), "it still examined as dead after it got up.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }
}
