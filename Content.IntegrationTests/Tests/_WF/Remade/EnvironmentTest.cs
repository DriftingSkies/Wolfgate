#nullable enable
using System.Collections.Generic;

using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Abilities;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Counterplay;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Environment;
using Content.Server._WF.Remade.Garrison;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Morgue;
using Content.Server._WF.Remade.Vents;
using Content.Server.NPC.HTN;
using Content.Server.Storage.Components;
using Content.Shared._WF.Remade;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

using static Content.IntegrationTests.Tests._WF.Remade.WFRemadeDirectorTest;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The ambient horror, the cold store, the vents and the garrison: that they happen, near whom and how fast.</summary>
[TestFixture]
public sealed class EnvironmentTest
{
    private static EntityUid SpawnScion(IEntityManager entMan, EntityCoordinates at, float resonance)
    {
        var scion = entMan.SpawnEntity("WFMonolithScion", at);
        entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
        entMan.GetComponent<WFMonolithComponent>(scion).Resonance = resonance;
        return scion;
    }

    private static List<EntityUid> Remade(IEntityManager entMan)
    {
        var found = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<WFRemadeComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!entMan.Deleted(uid))
                found.Add(uid);
        }

        return found;
    }

    /// <summary>The Remade that aren't a stone's guards.</summary>
    private static List<EntityUid> Runners(IEntityManager entMan)
    {
        return Remade(entMan).Where(uid => !entMan.HasComponent<WFRemadeGuardComponent>(uid)).ToList();
    }

    private static int DecorCount(IEntityManager entMan, bool countedOnly = false)
    {
        var count = 0;
        var query = entMan.AllEntityQueryEnumerator<WFEnvDecorComponent>();
        while (query.MoveNext(out var uid, out var decor))
        {
            if (!entMan.Deleted(uid) && (decor.Counted || !countedOnly))
                count++;
        }

        return count;
    }

    [Test]
    public async Task DecorGrowsOnCorruptionByRaptureAndGoesWithIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var lookup = entMan.System<EntityLookupSystem>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            CorruptionTest.LayDeck(pair, map, 12);
            // A ghost's body: the test map is a vacuum, and a body that needs air is dead long before the decor has grown.
            var watcher = entMan.SpawnEntity("MobObserver", At(map, -3, -3));
            server.PlayerMan.SetAttachedEntity(pair.Player!, watcher);
            // A console and a vent, so veins and a glyph screen have something to cover.
            entMan.SpawnEntity("ComputerAlert", At(map, 3, 3));
            entMan.SpawnEntity("GasVentPump", At(map, -4, 3));
            scion = SpawnScion(entMan, At(map, 0, 0), 80f);
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        for (var i = 0; i < 240 && (monolith.Corruption.Count < 300 || DecorCount(entMan) < 30); i++)
            await pair.RunTicksSync(60);

        var decor = 0;
        await server.WaitAssertion(() =>
        {
            decor = DecorCount(entMan);
            var state = entMan.GetComponent<WFEnvStateComponent>(scion);
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Rapture));
            Assert.That(decor, Is.GreaterThanOrEqualTo(30), $"only {decor} decor over {monolith.Corruption.Count} corruption tiles at Rapture.");
            Assert.That(state.Decor, Is.EqualTo(DecorCount(entMan, true)), "the Monolith's decor count drifted from the decor in the world.");
            Assert.That(decor, Is.LessThanOrEqualTo((int) MathF.Ceiling(monolith.Corruption.Count * WFEnvDecorSystem.Budget(monolith.Stage)) + 8), "decor went past its budget.");

            // Every piece sits on a corruption tile, and the lookup can find it, which the ambient noises rely on.
            var query = entMan.AllEntityQueryEnumerator<WFEnvDecorComponent, TransformComponent>();
            var checkedFound = false;
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                Assert.That(entMan.HasComponent<WFCorruptionComponent>(xform.ParentUid), Is.True, "a piece of decor isn't parented to a corruption tile.");
                if (checkedFound)
                    continue;

                checkedFound = true;
                var near = new HashSet<Entity<WFEnvDecorComponent>>();
                lookup.GetEntitiesInRange(xform.Coordinates, 1f, near);
                Assert.That(near.Any(found => found.Owner == uid), Is.True, "the entity lookup can't find decor that is parented to a tile.");
            }
        });

        // A crewman in the middle of it hears the odd wet noise.
        var env = entMan.GetComponent<WFEnvStateComponent>(scion);
        for (var i = 0; i < 90 && env.Noises == 0; i++)
            await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(env.Noises, Is.GreaterThan(0), "the crew heard nothing from the growth in 90 s.");
            TestContext.Out.WriteLine($"{env.Noises} noises, {env.Sparked} sparks heard");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entMan.DeleteEntity(scion);
        });

        for (var i = 0; i < 120 && (DecorCount(entMan) > 0); i++)
            await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(DecorCount(entMan), Is.Zero, "decor stayed behind after its corruption died.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADecorousBigHullAndWhatItCosts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var corruption = entMan.System<WFCorruptionSystem>();
        EntityUid scion = default;
        var hull = 0;

        await server.WaitPost(() =>
        {
            // The corruption test's hull: 50 by 50, 2500 tiles, in rooms ten across with a door in each wall.
            CorruptionTest.LayDeck(pair, map, 25);
            for (var x = -25; x < 25; x++)
            for (var y = -25; y < 25; y++)
            {
                var wallX = ((x % 10) + 10) % 10 == 0;
                var wallY = ((y % 10) + 10) % 10 == 0;
                if (wallX && wallY)
                    entMan.SpawnEntity("WallSolid", At(map, x, y));
                else if (wallX)
                    entMan.SpawnEntity(((y % 10) + 10) % 10 == 5 ? "Airlock" : "WallSolid", At(map, x, y));
                else if (wallY)
                    entMan.SpawnEntity(((x % 10) + 10) % 10 == 5 ? "Airlock" : "WallSolid", At(map, x, y));
            }

            hull = corruption.HullTiles(map.Grid.Owner);
            scion = SpawnScion(entMan, At(map, 2, 2), 80f);
        });

        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        
        var seconds = 0;
        for (; seconds < 700 && monolith.Corruption.Count < hull - 40; seconds++)
            await pair.RunTicksSync(60);

        // Give the last slices a few passes to decorate, then time the settled hull against how it ran before the decor.
        await pair.RunSeconds(20f);
        var settled = System.Diagnostics.Stopwatch.StartNew();
        await server.WaitRunTicks(300);
        var msTick = settled.Elapsed.TotalMilliseconds / 300d;

        await server.WaitAssertion(() =>
        {
            var state = entMan.GetComponent<WFEnvStateComponent>(scion);
            var walls = 0;
            var query = entMan.AllEntityQueryEnumerator<WFEnvDecorComponent, MetaDataComponent>();
            var byProto = new Dictionary<string, int>();
            while (query.MoveNext(out _, out _, out var meta))
            {
                var id = meta.EntityPrototype?.ID ?? "?";
                byProto[id] = byProto.GetValueOrDefault(id) + 1;
                if (id.StartsWith("WFEnvCocoon"))
                    walls++;
            }

            TestContext.Out.WriteLine($"hull {hull} tiles, corruption {monolith.Corruption.Count} after {seconds} s; decor {DecorCount(entMan)} "
                                      + $"(budget {WFEnvDecorSystem.Budget(monolith.Stage):P0} of corruption), cocoons {walls}, ms a tick settled {msTick:F3}; "
                                      + string.Join(", ", byProto.OrderByDescending(pair => pair.Value).Select(pair => $"{pair.Key} {pair.Value}")));
            Assert.That(state.Decor, Is.GreaterThan(100), "a big hull at Rapture barely had any decor.");
            entMan.DeleteEntity(scion);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABodyInAMorgueTurnsFastAndBurstsOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var containers = entMan.System<SharedContainerSystem>();
        EntityUid scion = default;
        EntityUid morgue = default;
        EntityUid safeMorgue = default;
        EntityUid body = default;
        EntityUid safeBody = default;

        await server.WaitPost(() =>
        {
            CorruptionTest.LayDeck(pair, map, 12);
            // A Hymn stone, no corruption anywhere: the cold store turns on its own.
            scion = SpawnScion(entMan, At(map, 6, 6), 20f);

            EntityUid Load(float x, bool poisoned, out EntityUid tray)
            {
                tray = entMan.SpawnEntity("Morgue", At(map, x, -4));
                var corpse = entMan.SpawnEntity("MobHuman", At(map, x, -8));
                entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
                if (poisoned)
                    entMan.AddComponent<WFNecrotoxinProtectedComponent>(corpse);

                Assert.That(containers.Insert(corpse, entMan.GetComponent<EntityStorageComponent>(tray).Contents), Is.True, "the body wouldn't go into the tray.");
                return corpse;
            }

            body = Load(-6, false, out morgue);
            safeBody = Load(-2, true, out safeMorgue);
        });

        var morgues = entMan.System<WFMorgueSystem>();
        var startTime = server.ResolveDependency<IGameTiming>().CurTime;
        // The scan runs every 2 s on the 0.5 s update, so exactly 2 s can end a tick before it.
        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFMorgueTurningComponent>(body), Is.True, "the body in the tray wasn't marked to turn.");
            Assert.That(entMan.HasComponent<WFMorgueTurningComponent>(safeBody), Is.False, "necrotoxin didn't protect the body in the second tray.");
        });

        // A Hymn stone only makes the body knock; it bursts once the stone reaches Growth.
        for (var i = 0; i < (int) WFMorgueSystem.TurnMax + 4; i++)
            await pair.RunTicksSync(60);

        await server.WaitPost(() =>
        {
            Assert.That(entMan.Deleted(body), Is.False, "the body burst out under a Hymn stone.");
            Assert.That(entMan.GetComponent<EntityStorageComponent>(morgue).Open, Is.False, "the tray burst open under a Hymn stone.");
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
        });

        // Well inside the 90 seconds of lying on corruption, and the tray comes open.
        var limit = 20;
        for (var i = 0; i < limit && !entMan.Deleted(body); i++)
            await pair.RunTicksSync(60);

        var elapsed = TimeSpan.Zero;
        await server.WaitAssertion(() =>
        {
            elapsed = server.ResolveDependency<IGameTiming>().CurTime - startTime;
            Assert.That(entMan.Deleted(body), Is.True, $"the body never turned in {elapsed.TotalSeconds:F0} s.");
            Assert.That(entMan.GetComponent<EntityStorageComponent>(morgue).Open, Is.True, "the tray didn't burst open.");
            Assert.That(morgues.Bursts, Is.EqualTo(1));
            var remade = Remade(entMan);
            Assert.That(remade, Has.Count.EqualTo(1), "no Remade climbed out.");
            var transforms = entMan.System<SharedTransformSystem>();
            var away = (transforms.GetWorldPosition(remade[0]) - transforms.GetWorldPosition(morgue)).Length();
            // It may already have taken a step by the time we look.
            Assert.That(away, Is.LessThan(5f), "the Remade didn't come out at the tray.");
            Assert.That(entMan.GetComponent<WFRemadeComponent>(remade[0]).Source, Is.EqualTo(scion), "it didn't answer to the stone.");
            Assert.That(entMan.GetComponent<EntityStorageComponent>(safeMorgue).Open, Is.False, "the protected tray burst too.");
            Assert.That(entMan.Deleted(safeBody), Is.False);
            TestContext.Out.WriteLine($"morgue body turned and burst out after {elapsed.TotalSeconds:F1} s of game time");
        });

        await server.WaitPost(() =>
        {
            foreach (var remade in Remade(entMan))
                entMan.DeleteEntity(remade);

            entMan.DeleteEntity(scion);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARemadeSentThroughTheVentsComesOutOfAnotherVent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var directorSystem = entMan.System<WFRemadeDirectorSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid scion = default;
        EntityUid crewman = default;
        EntityUid origin = default;
        EntityUid exit = default;
        EntityUid runner = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            // Twenty metres from the exit vent and more than the vents' reach from the rest, so the exit is the only one it can pick.
            crewman = SpawnCrewman(pair, At(map, 10, -20));
            scion = SpawnScion(entMan, At(map, -18, -18), 60f);
            // Both out of the crewman's sight, one far corner to the far side, one vent in between for the rattle.
            origin = entMan.SpawnEntity("GasVentPump", At(map, -20, 21));
            entMan.SpawnEntity("GasVentPump", At(map, 9, 9));
            exit = entMan.SpawnEntity("GasVentPump", At(map, 16, 0));
            directorSystem.ForgetVents();
        });

        await pair.RunSeconds(2f);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        await server.WaitPost(() => director.Biomass = 600f);

        var sent = false;
        await server.WaitPost(() => sent = directorSystem.TryVentRun((scion, director, monolith), false, timing.CurTime));
        await server.WaitAssertion(() =>
        {
            Assert.That(sent, Is.True, "the Director wouldn't send a runner through the vents.");
            var remade = Runners(entMan).Where(uid => entMan.HasComponent<WFRemadeVentTripComponent>(uid)).ToList();
            Assert.That(remade, Has.Count.EqualTo(1), "the run should put exactly one Remade on a trip.");
            runner = remade[0];
            Assert.That(entMan.System<SharedContainerSystem>().IsEntityInContainer(runner), Is.True, "a runner on a trip should be out of the world.");
        });

        await RunUntil(pair, () => !entMan.HasComponent<WFRemadeVentTripComponent>(runner), 40);
        await server.WaitAssertion(() =>
        {
            var remade = runner;
            Assert.That(entMan.HasComponent<WFRemadeVentTripComponent>(remade), Is.False, "the runner never came out.");
            // A leaper is across the room before the test looks, so the vent it used is read from what it recorded, not from where it stands.
            Assert.That(exit, Is.Not.EqualTo(origin));
            Assert.That((At(map, 16, 0).Position - At(map, -20, 21).Position).Length(), Is.GreaterThan(20f), "it came out of the vent it went into.");
            Assert.That(entMan.GetComponent<WFRemadeBehaviourComponent>(remade).ArrivalVent, Is.EqualTo(exit));
            Assert.That(director.Rattles, Is.GreaterThan(0), "the crew heard no rattle on the way.");
            Assert.That(entMan.GetComponent<WFVentStateComponent>(scion).Runs, Is.EqualTo(1));
            Assert.That(entMan.Deleted(exit) || entMan.TryGetComponent<DamageableComponent>(exit, out var dented) && dented.TotalDamage > 0, Is.True, "the vent it came out of wasn't dented or torn out.");
            foreach (var runner in Remade(entMan))
                entMan.DeleteEntity(runner);

            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            entMan.DeleteEntity(scion);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARemadeAmbushingFromAVentBurstsOutAtACrewmanWithinThreeMetres()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var directorSystem = entMan.System<WFRemadeDirectorSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var abilities = entMan.System<WFAbilitySystem>();
        EntityUid scion = default;
        EntityUid crewman = default;
        EntityUid origin = default;
        EntityUid lair = default;
        EntityUid leaper = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = SpawnScion(entMan, At(map, -18, -18), 60f);
            origin = entMan.SpawnEntity("GasVentPump", At(map, -20, 21));
            lair = entMan.SpawnEntity("GasVentPump", At(map, 16, 0));
            directorSystem.ForgetVents();
            leaper = entMan.SpawnEntity("WFRemadeLeaper", entMan.GetComponent<TransformComponent>(origin).Coordinates);
            directorSystem.Enlist(leaper, scion);
            Assert.That(directorSystem.TryVentTravel(leaper, origin, lair), Is.True, "the leaper wouldn't enter the vents.");
            entMan.GetComponent<WFRemadeVentTripComponent>(leaper).Ambush = true;
        });

        await pair.RunSeconds(1f);
        await RunUntil(pair, () => entMan.HasComponent<WFRemadeVentAmbushComponent>(leaper), 40);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadeVentAmbushComponent>(leaper), Is.True, "the leaper never settled in the lair.");
            Assert.That(containers.IsEntityInContainer(leaper), Is.True, "an ambusher should be inside the vent.");
        });

        // A crewman walking by at eight metres hears nothing and sees nothing.
        await server.WaitPost(() => transforms.SetCoordinates(crewman, At(map, 8, 0)));
        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadeVentAmbushComponent>(leaper), Is.True, "it burst out at a crewman eight metres away.");
            Assert.That(containers.IsEntityInContainer(leaper), Is.True);
        });

        var timingNow = server.ResolveDependency<IGameTiming>().CurTime;
        var leap = abilities.Find<WFLeapAbility>(leaper)!;
        var state = entMan.GetComponent<WFVentStateComponent>(scion);
        await server.WaitPost(() => transforms.SetCoordinates(crewman, At(map, 14, 0)));
        await RunUntil(pair, () => !containers.IsEntityInContainer(leaper), 10);

        await server.WaitAssertion(() =>
        {
            var ambush = entMan.TryGetComponent<WFRemadeVentAmbushComponent>(leaper, out var lurking) ? $"still lurking, burst at {lurking.BurstAt}, target {lurking.Target}, now {timingNow}" : "no ambush component";
            Assert.That(containers.IsEntityInContainer(leaper), Is.False, $"the ambusher stayed in the vent with a crewman two metres off ({ambush}; crewman at {transforms.GetWorldPosition(crewman)} {entMan.System<MobStateSystem>().IsDead(crewman)}, vent at {At(map, 16, 0).Position}).");
            Assert.That(state.Bursts, Is.EqualTo(1));
            Assert.That((transforms.GetWorldPosition(leaper) - At(map, 16, 0).Position).Length(), Is.LessThan(5f));
            Assert.That(entMan.Deleted(lair) || entMan.TryGetComponent<DamageableComponent>(lair, out var dented) && dented.TotalDamage > 0, Is.True, "the vent wasn't dented or torn out.");
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            Assert.That(leap.Leaps, Is.GreaterThan(0), "it didn't leap at the crewman.");
            Assert.That((transforms.GetWorldPosition(leaper) - transforms.GetWorldPosition(crewman)).Length(), Is.LessThan(4f), "it didn't land on the crewman.");
            entMan.DeleteEntity(leaper);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            entMan.DeleteEntity(scion);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AHarvestStoneKeepsAGarrisonAndCallsEverythingToWhoeverHurtsIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var damageable = entMan.System<DamageableSystem>();
        var proto = server.ResolveDependency<IPrototypeManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid scion = default;
        EntityUid crewman = default;
        EntityUid stray = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            // The crewman is far from the stone, so its guards have no one to fight yet.
            crewman = SpawnCrewman(pair, At(map, -20, -20));
            scion = SpawnScion(entMan, At(map, 3, 0), 60f);
        });

        await RunUntil(pair, () => entMan.TryGetComponent<WFGarrisonComponent>(scion, out var found) && found.Guards.Count >= WFRemadeDirectorSystem.GarrisonSize(WFMonolithStage.Harvest, false), 90);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFGarrisonComponent>(scion);
            var want = WFRemadeDirectorSystem.GarrisonSize(WFMonolithStage.Harvest, false);
            Assert.That(comp.Guards, Has.Count.EqualTo(want), $"the garrison at Harvest should be {want}.");
            foreach (var guard in comp.Guards)
            {
                var distance = (transforms.GetWorldPosition(guard) - transforms.GetWorldPosition(scion)).Length();
                Assert.That(distance, Is.LessThan(16f), "a guard wandered off from the stone.");
                Assert.That(entMan.GetComponent<WFRemadeBehaviourComponent>(guard).Mode, Is.EqualTo(WFRemadeMode.Guard));
            }

            Assert.That(entMan.GetComponent<WFRemadeDirectorComponent>(scion).Horde, Is.Empty, "the garrison should be kept apart from the hunting horde.");
        });

        // A guard that dies is replaced soon.
        EntityUid fallen = default;
        await server.WaitPost(() =>
        {
            fallen = entMan.GetComponent<WFGarrisonComponent>(scion).Guards.First();
            entMan.System<MobStateSystem>().ChangeMobState(fallen, MobState.Dead);
        });
        await RunUntil(pair, () => entMan.GetComponent<WFGarrisonComponent>(scion).Guards.Count >= 4 && !entMan.GetComponent<WFGarrisonComponent>(scion).Guards.Contains(fallen), 30);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFGarrisonComponent>(scion);
            Assert.That(comp.Guards, Has.Count.EqualTo(4), "a lost guard wasn't replaced.");
            Assert.That(comp.Raised, Is.GreaterThanOrEqualTo(5));
        });

        // A hunter far from the stone, busy with something else.
        await server.WaitPost(() =>
        {
            stray = entMan.SpawnEntity("WFRemadeSlasher", At(map, -18, 12));
            entMan.System<WFRemadeDirectorSystem>().Enlist(stray, scion);
        });

        await server.WaitPost(() =>
        {
            var blunt = proto.Index<DamageTypePrototype>("Blunt");
            damageable.TryChangeDamage(scion, new DamageSpecifier(blunt, 40), ignoreResistances: true, origin: crewman);
        });

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFGarrisonComponent>(scion);
            Assert.That(comp.Calls, Is.GreaterThanOrEqualTo(1), "the stone wasn't heard being hurt.");
            var answering = 0;
            foreach (var uid in Remade(entMan))
            {
                if (entMan.GetComponent<WFRemadeBehaviourComponent>(uid).Mode == WFRemadeMode.Hidden || entMan.System<MobStateSystem>().IsDead(uid))
                    continue;

                var htn = entMan.GetComponent<HTNComponent>(uid);
                Assert.That(htn.Blackboard.TryGetValue<EntityUid>("Target", out var target, entMan) && target == crewman, Is.True,
                    $"{entMan.ToPrettyString(uid)} wasn't turned on the attacker.");
                answering++;
            }

            Assert.That(answering, Is.GreaterThanOrEqualTo(comp.Guards.Count + 1), "the cry didn't reach the guards and the stray hunter too.");
            TestContext.Out.WriteLine($"{answering} Remade turned on the attacker, {comp.Guards.Count} of them garrison, the stray {(transforms.GetWorldPosition(stray) - transforms.GetWorldPosition(scion)).Length():F0} m off");

            foreach (var uid in Remade(entMan))
                entMan.DeleteEntity(uid);

            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            entMan.DeleteEntity(scion);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARemadeDragsACorpseIntoAVentAndItTurnsUpElsewhere()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var directorSystem = entMan.System<WFRemadeDirectorSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid scion = default;
        EntityUid corpse = default;
        EntityUid hauler = default;
        EntityUid tray = default;
        EntityUid ventIn = default;
        EntityUid exit = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            server.CfgMan.SetCVar(WFGarrisonCVars.Enabled, false);
            LayDeck(pair, map);
            // The only crewman is far off and can't see any of it, but near enough to keep the Director and the Remade awake.
            SpawnCrewman(pair, At(map, 20, -20));
            scion = SpawnScion(entMan, At(map, -18, -18), 60f);
            corpse = entMan.SpawnEntity("MobHuman", At(map, -8, 12));
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            ventIn = entMan.SpawnEntity("GasVentPump", At(map, -6, 12));
            exit = entMan.SpawnEntity("GasVentPump", At(map, 8, 14));
            tray = entMan.SpawnEntity("Morgue", At(map, 8, 10));
            hauler = entMan.SpawnEntity("WFRemadeSlasher", At(map, -12, 12));
            directorSystem.Enlist(hauler, scion);
            directorSystem.ForgetVents();
        });

        await pair.RunSeconds(2f);
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
        var started = false;
        // Where the body goes is picked by chance, and a pick with no vent near it is turned down, so try a few times.
        for (var attempt = 0; attempt < 30 && !started; attempt++)
            await server.WaitPost(() => started = directorSystem.TryHaul((scion, director, monolith), timing.CurTime));

        await server.WaitAssertion(() =>
        {
            Assert.That(started, Is.True, "the Director wouldn't send anyone for the corpse.");
            Assert.That(entMan.HasComponent<WFRemadeHaulComponent>(hauler), Is.True);
        });

        var state = entMan.GetComponent<WFVentStateComponent>(scion);
        await RunUntil(pair, () => state.Hauls > 0, 90);

        await server.WaitAssertion(() =>
        {
            Assert.That(state.Hauls, Is.EqualTo(1), $"the corpse was never carried through the vents ({Describe(pair, hauler)}).");
            Assert.That(entMan.HasComponent<WFRemadeHaulComponent>(hauler), Is.False);
            var inTray = containers.TryGetContainingContainer(corpse, out var holder) && holder.Owner == tray;
            var moved = !entMan.Deleted(corpse) && (transforms.GetWorldPosition(corpse) - transforms.GetWorldPosition(ventIn)).Length() > 12f;
            Assert.That(entMan.Deleted(corpse) || inTray || moved, Is.True, "the corpse is still where it lay.");
            Assert.That((transforms.GetWorldPosition(hauler) - transforms.GetWorldPosition(ventIn)).Length(), Is.GreaterThan(8f), "the hauler came out where it went in.");
            TestContext.Out.WriteLine($"hauled corpse {(entMan.Deleted(corpse) ? "already turned" : inTray ? "now in the tray" : "now on the floor")}");

            foreach (var remade in Remade(entMan))
                entMan.DeleteEntity(remade);

            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            entMan.DeleteEntity(scion);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AFakeBodyInAMorgueTrayRisesWhenACrewmanComesNear()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var directorSystem = entMan.System<WFRemadeDirectorSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        EntityUid scion = default;
        EntityUid crewman = default;
        EntityUid tray = default;
        EntityUid fake = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, -18, -18));
            scion = SpawnScion(entMan, At(map, -20, 20), 0f);
            tray = entMan.SpawnEntity("Morgue", At(map, 4, 0));
            fake = entMan.SpawnEntity("WFRemadeSlasher", At(map, 4, 0));
            directorSystem.Enlist(fake, scion);
            directorSystem.PlayDead(fake);
            Assert.That(directorSystem.HideInTray(fake, tray), Is.True, "the fake wouldn't go into the tray.");
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadePlayingDeadComponent>(fake), Is.True, "the fake rose with nobody near.");
            Assert.That(containers.IsEntityInContainer(fake), Is.True, "the fake left its tray.");
            Assert.That(entMan.GetComponent<EntityStorageComponent>(tray).Open, Is.False);
            transforms.SetCoordinates(crewman, At(map, 6, 0));
        });

        await RunUntil(pair, () => !entMan.HasComponent<WFRemadePlayingDeadComponent>(fake), 5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadePlayingDeadComponent>(fake), Is.False, "the fake didn't rise when a crewman came within range of the tray.");
            Assert.That(entMan.GetComponent<EntityStorageComponent>(tray).Open, Is.True, "the tray didn't burst open.");
            Assert.That(containers.IsEntityInContainer(fake), Is.False, "the fake is still in the tray.");
            Assert.That(entMan.System<MobStateSystem>().IsAlive(fake), Is.True);
            entMan.DeleteEntity(fake);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            entMan.DeleteEntity(scion);
        });

        await pair.CleanReturnAsync();
    }
}
