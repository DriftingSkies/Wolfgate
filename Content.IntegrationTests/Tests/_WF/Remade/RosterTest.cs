#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Abilities;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Limbs;
using Content.Server._WF.Remade.Roster;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Stunnable;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The common Remade: each one spawns, hunts and hurts, and the leap, burst, spit, vomit and shell do what they say.</summary>
[TestFixture]
public sealed class RosterTest
{
    private const string Hopper = "WFRemadeHopper";
    private const string Leaper = "WFRemadeLeaper";
    private const string LeaperEnhanced = "WFRemadeLeaperEnhanced";
    private const string Spitter = "WFRemadeSpitter";
    private const string Exploder = "WFRemadeExploder";
    private const string ExploderEnhanced = "WFRemadeExploderEnhanced";
    private const string Puker = "WFRemadePuker";
    private const string Lurker = "WFRemadeLurker";

    private static readonly string[] All = { Hopper, Leaper, LeaperEnhanced, Spitter, Exploder, ExploderEnhanced, Puker, Lurker };

    private static float Distance(SharedTransformSystem transforms, EntityUid a, EntityUid b)
    {
        return (transforms.GetWorldPosition(a) - transforms.GetWorldPosition(b)).Length();
    }

    private static float Caustic(IEntityManager entMan, EntityUid uid)
    {
        return entMan.Deleted(uid) ? 0f : entMan.GetComponent<DamageableComponent>(uid).Damage.DamageDict.GetValueOrDefault("Caustic").Float();
    }

    [Test]
    public async Task EveryRosterMobSpawnsWithItsPartsAndItsSpriteStatesExist()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mobs = new Dictionary<string, EntityUid>();

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            var x = -8;
            foreach (var id in All)
            {
                mobs[id] = entMan.SpawnEntity(id, WFRemadeDirectorTest.At(map, x, 3));
                x += 3;
            }
        });
        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            foreach (var (id, uid) in mobs)
            {
                var limbs = entMan.GetComponent<WFRemadeLimbsComponent>(uid);
                Assert.That(entMan.HasComponent<WFRemadeAbilitiesComponent>(uid), Is.True, $"{id} has no abilities.");
                Assert.That(entMan.HasComponent<WFRemadeVoiceComponent>(uid), Is.True, $"{id} has no voice.");
                Assert.That(limbs.Limbs.Any(limb => !limb.Severable), Is.True, $"{id} has no torso.");
                Assert.That(entMan.System<MobStateSystem>().IsAlive(uid), Is.True, $"{id} didn't spawn alive.");
            }
        });

        var sprites = pair.Client.System<SpriteSystem>();
        var cache = pair.Client.ResolveDependency<IResourceCache>();
        await pair.Client.WaitAssertion(() =>
        {
            foreach (var (id, uid) in mobs)
            {
                var client = pair.ToClientUid(uid);
                var sprite = pair.Client.EntMan.GetComponent<SpriteComponent>(client);
                var limbs = pair.Client.EntMan.GetComponent<WFRemadeLimbsComponent>(client);
                var ent = new Entity<SpriteComponent?>(client, sprite);
                Assert.That(cache.TryGetResource<RSIResource>(new ResPath("/Textures") / limbs.Rsi, out var rsi), Is.True, $"{id}: no sprite sheet {limbs.Rsi}.");

                foreach (var limb in limbs.Limbs)
                {
                    foreach (var key in limb.LayerKeys)
                    {
                        Assert.That(sprites.LayerMapTryGet(ent, key, out var index, false), Is.True, $"{id}: no layer '{key}' for the {limb.Id}.");
                        var state = sprites.LayerGetRsiState(ent, index).Name ?? string.Empty;
                        Assert.That(rsi!.RSI.TryGetState(state, out _), Is.True, $"{id}: layer '{key}' uses state '{state}', which the sheet doesn't have.");
                    }

                    if (limb.Lying != null)
                        Assert.That(rsi!.RSI.TryGetState(limb.Lying, out _), Is.True, $"{id}: the lying state '{limb.Lying}' of the {limb.Id} doesn't exist.");
                }

                Assert.That(sprites.LayerMapTryGet(ent, limbs.DeadLayer, out var dead, false), Is.True, $"{id}: no dead layer.");
                var deadState = sprites.LayerGetRsiState(ent, dead).Name ?? string.Empty;
                Assert.That(rsi!.RSI.TryGetState(deadState, out _), Is.True, $"{id}: dead layer uses state '{deadState}', which the sheet doesn't have.");
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var uid in mobs.Values)
                entMan.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryRosterMobWalksToACrewmanAndHurtsIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var failures = new List<string>();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
        });

        foreach (var id in All)
        {
            EntityUid crewman = default;
            EntityUid mob = default;
            await server.WaitPost(() =>
            {
                crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
                mob = entMan.SpawnEntity(id, WFRemadeDirectorTest.At(map, 9, 0));
            });

            await WFRemadeDirectorTest.RunUntil(pair, () => LimbsTest.Total(pair, crewman) > 0f || entMan.Deleted(crewman), 45);
            await server.WaitPost(() =>
            {
                if (!entMan.Deleted(crewman) && LimbsTest.Total(pair, crewman) <= 0f)
                    failures.Add(id);

                entMan.DeleteEntity(mob);
                entMan.DeleteEntity(crewman);
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(failures, Is.Empty, $"these never hurt a crewman 9 tiles away: {string.Join(", ", failures)}.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnExploderBlowsUpNearATargetAndDiesInItsOwnBlast()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid crewman = default;
        EntityUid exploder = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            exploder = entMan.SpawnEntity(Exploder, WFRemadeDirectorTest.At(map, 9, 0));
        });

        var comp = entMan.GetComponent<WFRemadeExploderComponent>(exploder);
        var ability = entMan.System<WFAbilitySystem>().Find<WFExplodeAbility>(exploder)!;
        await WFRemadeDirectorTest.RunUntil(pair, () => comp.Detonated, 45);
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            Assert.That(ability.Lit, Is.GreaterThan(0), "it never lit its fuse.");
            Assert.That(comp.Detonated, Is.True, "it never blew up.");
            Assert.That(entMan.System<MobStateSystem>().IsDead(exploder), Is.True, "it survived its own blast.");
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(exploder).Severed, Does.Contain("l_arm"), "the pustule is still on.");
            Assert.That(entMan.Deleted(crewman) || LimbsTest.Total(pair, crewman) > 0f, Is.True, "the blast didn't touch the crewman it walked up to.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnExploderWithNoPustuleNeverBlowsUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid crewman = default;
        EntityUid exploder = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            exploder = entMan.SpawnEntity(Exploder, WFRemadeDirectorTest.At(map, 9, 0));
            Assert.That(entMan.System<WFRemadeLimbsSystem>().TrySever(exploder, "l_arm"), Is.True);
        });

        var comp = entMan.GetComponent<WFRemadeExploderComponent>(exploder);
        var ability = entMan.System<WFAbilitySystem>().Find<WFExplodeAbility>(exploder)!;
        // It steps back after a swing, so note that it got there rather than where it ends up.
        var reached = false;
        await WFRemadeDirectorTest.RunUntil(pair, () => reached = Distance(transforms, exploder, crewman) < 2f, 45);
        await pair.RunSeconds(6f);

        await server.WaitAssertion(() =>
        {
            Assert.That(reached, Is.True, "it never walked up to the crewman.");
            Assert.That(ability.Lit, Is.Zero, "it lit a fuse with no pustule.");
            Assert.That(comp.Detonated, Is.False, "it blew up with no pustule.");
            Assert.That(entMan.System<MobStateSystem>().IsAlive(exploder), Is.True);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShootingThePustuleBurstsItButCuttingItOffUpCloseDoesNot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid shot = default;
        EntityUid cut = default;
        EntityUid bystander = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            var shooter = entMan.SpawnEntity("MobCarp", WFRemadeDirectorTest.At(map, 9, 0));
            shot = entMan.SpawnEntity(Exploder, WFRemadeDirectorTest.At(map, 0, 0));
            bystander = entMan.SpawnEntity("MobCarp", WFRemadeDirectorTest.At(map, 1, 1));
            LimbsTest.Hit(pair, shot, "Piercing", 40, TargetBodyPart.LeftArm, shooter);

            var near = entMan.SpawnEntity("MobCarp", WFRemadeDirectorTest.At(map, -10, -9));
            cut = entMan.SpawnEntity(Exploder, WFRemadeDirectorTest.At(map, -10, -10));
            LimbsTest.Hit(pair, cut, "Slash", 40, TargetBodyPart.LeftArm, near);
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(cut).Severed, Does.Contain("l_arm"), "the cut didn't take the pustule off.");
        });
        await pair.RunSeconds(4f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFRemadeExploderComponent>(shot).Detonated, Is.True, "a shot pustule didn't burst.");
            Assert.That(entMan.System<MobStateSystem>().IsDead(shot), Is.True, "the exploder lived through its own burst.");
            Assert.That(entMan.Deleted(bystander) || LimbsTest.Total(pair, bystander) > 0f, Is.True, "the burst didn't touch someone a tile away.");
            Assert.That(entMan.GetComponent<WFRemadeExploderComponent>(cut).Detonated, Is.False, "a pustule cut off up close still burst.");
            Assert.That(entMan.System<MobStateSystem>().IsAlive(cut), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnExploderShotInARowBlastsItsNeighboursWithoutBreakingAnything()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var row = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            var shooter = entMan.SpawnEntity("MobCarp", WFRemadeDirectorTest.At(map, 12, 0));
            for (var i = 0; i < 5; i++)
                row.Add(entMan.SpawnEntity(i % 2 == 0 ? Exploder : ExploderEnhanced, WFRemadeDirectorTest.At(map, i, 0)));

            LimbsTest.Hit(pair, row[0], "Piercing", 40, TargetBodyPart.LeftArm, shooter);
        });
        await pair.RunSeconds(10f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFRemadeExploderComponent>(row[0]).Detonated, Is.True, "the shot one didn't burst.");
            Assert.That(entMan.Deleted(row[1]) || LimbsTest.Total(pair, row[1]) > 0f, Is.True, "the blast didn't touch the exploder beside it.");
            Assert.That(entMan.Deleted(row[2]) || LimbsTest.Total(pair, row[2]) > 0f, Is.True, "the blast didn't reach two tiles.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnExploderKilledWhileItsFuseBurnsNeverBlowsUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid crewman = default;
        EntityUid exploder = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            exploder = entMan.SpawnEntity(Exploder, WFRemadeDirectorTest.At(map, 9, 0));
        });

        var comp = entMan.GetComponent<WFRemadeExploderComponent>(exploder);
        var ability = entMan.System<WFAbilitySystem>().Find<WFExplodeAbility>(exploder)!;
        await WFRemadeDirectorTest.RunUntil(pair, () => ability.Lit > 0, 45);
        await server.WaitAssertion(() =>
        {
            Assert.That(ability.Lit, Is.GreaterThan(0), "it never lit its fuse.");
            Assert.That(comp.Detonated, Is.False);
            entMan.System<MobStateSystem>().ChangeMobState(exploder, MobState.Dead);
        });

        await pair.RunSeconds(5f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.System<MobStateSystem>().IsDead(exploder), Is.True, "it lived through a killing blow.");
            Assert.That(comp.Detonated, Is.False, "a dead exploder blew up.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AHordeOfExplodersBlowsUpAroundACrewmanAndSurvivesItsOwnBlasts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var horde = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            for (var i = 0; i < 4; i++)
                horde.Add(entMan.SpawnEntity(i == 3 ? ExploderEnhanced : Exploder, WFRemadeDirectorTest.At(map, 10, i * 2 - 3)));
        });

        bool Went(EntityUid uid) => entMan.Deleted(uid) || entMan.GetComponent<WFRemadeExploderComponent>(uid).Detonated;
        await WFRemadeDirectorTest.RunUntil(pair, () => horde.Any(Went), 60);
        await pair.RunSeconds(8f);

        await server.WaitAssertion(() =>
        {
            Assert.That(horde.Any(Went), Is.True, "none of them blew up.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALeaperLeapsAtATargetAcrossTheRoomButNotWithoutItsTail()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var abilities = entMan.System<WFAbilitySystem>();
        EntityUid crewman = default;
        EntityUid leaper = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            leaper = entMan.SpawnEntity(Leaper, WFRemadeDirectorTest.At(map, 10, 0));
        });

        var leap = abilities.Find<WFLeapAbility>(leaper)!;
        await WFRemadeDirectorTest.RunUntil(pair, () => leap.Hit, 40);
        await server.WaitAssertion(() =>
        {
            Assert.That(leap.Leaps, Is.GreaterThan(0), "it never leapt.");
            Assert.That(leap.Hit, Is.True, "it leapt and didn't land on the crewman.");
            Assert.That(LimbsTest.Total(pair, crewman), Is.GreaterThan(0f), "the leap did no damage.");
        });

        EntityUid maimed = default;
        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(leaper);
            entMan.DeleteEntity(crewman);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            maimed = entMan.SpawnEntity(Leaper, WFRemadeDirectorTest.At(map, 7, 0));
            var limbs = entMan.System<WFRemadeLimbsSystem>();
            Assert.That(limbs.TrySever(maimed, "l_leg"), Is.True);
            Assert.That(limbs.TrySever(maimed, "r_leg"), Is.True);
            Assert.That(limbs.Can(maimed, WFRemadeLimbStat.Leap), Is.False, "a leaper with no tail can still leap.");
        });

        var maimedLeap = abilities.Find<WFLeapAbility>(maimed)!;
        var maimedStab = abilities.Find<WFTailStabAbility>(maimed)!;
        await pair.RunSeconds(1f);
        await WFRemadeDirectorTest.RunUntil(pair, () => false, 15);

        await server.WaitAssertion(() =>
        {
            Assert.That(maimedLeap.Leaps, Is.Zero, "a leaper with no tail leapt.");
            Assert.That(maimedStab.Stabs, Is.Zero, "a leaper with no tail stabbed with it.");
            Assert.That(entMan.System<MobStateSystem>().IsAlive(maimed), Is.True);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASpitterSpitsFromRangeAndBacksOffWhenPressed()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var abilities = entMan.System<WFAbilitySystem>();
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid crewman = default;
        EntityUid far = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            far = entMan.SpawnEntity(Spitter, WFRemadeDirectorTest.At(map, 10, 0));
        });

        var shots = entMan.GetComponent<WFRemadeAbilitiesComponent>(far).Abilities.OfType<WFShootAbility>().ToList();
        await WFRemadeDirectorTest.RunUntil(pair, () => shots.Sum(shot => shot.Shots) > 0, 40);
        float rangeAtFirstShot = 0f;
        await server.WaitPost(() => rangeAtFirstShot = shots.Where(shot => shot.Shots > 0).Max(shot => shot.LastRange));
        await WFRemadeDirectorTest.RunUntil(pair, () => Caustic(entMan, crewman) > 0f, 20);

        await server.WaitAssertion(() =>
        {
            Assert.That(shots.Sum(shot => shot.Shots), Is.GreaterThan(0), "it never spat.");
            Assert.That(rangeAtFirstShot, Is.GreaterThan(5f), "it came right up to the crewman before it spat.");
            Assert.That(Distance(transforms, far, crewman), Is.GreaterThan(4f), "it walked into melee instead of standing off.");
            Assert.That(Caustic(entMan, crewman), Is.GreaterThan(0f), "the acid never landed.");
        });

        EntityUid close = default;
        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(far);
            entMan.DeleteEntity(crewman);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            close = entMan.SpawnEntity(Spitter, WFRemadeDirectorTest.At(map, 2, 0));
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => Distance(transforms, close, crewman) > 4.5f, 30);
        await server.WaitAssertion(() =>
        {
            Assert.That(Distance(transforms, close, crewman), Is.GreaterThan(4f), "a spitter with a crewman in its face didn't back off.");
            Assert.That(entMan.System<MobStateSystem>().IsAlive(close), Is.True);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task APukerCoatsACrewmanInAcidAndSlowsThem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var abilities = entMan.System<WFAbilitySystem>();
        EntityUid victim = default;
        EntityUid puker = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            // Far off and out of sight, but alive, so the puker's AI stays awake.
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, -20, -20));
            victim = entMan.SpawnEntity("MobHuman", WFRemadeDirectorTest.At(map, 0, 0));
            puker = entMan.SpawnEntity(Puker, WFRemadeDirectorTest.At(map, 4, 0));
        });

        var vomit = abilities.Find<WFVomitAbility>(puker)!;
        await WFRemadeDirectorTest.RunUntil(pair, () => vomit.Hits > 0, 30);
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(vomit.Vomits, Is.GreaterThan(0), "it never vomited.");
            Assert.That(vomit.Hits, Is.GreaterThan(0), "the vomit hit nobody.");
            Assert.That(Caustic(entMan, victim), Is.GreaterThan(0f), "the acid did no damage.");
            Assert.That(entMan.HasComponent<SlowedDownComponent>(victim), Is.True, "the acid didn't slow them.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALurkerOpensToFireSpinesAndCurlsUpWhenHit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var abilities = entMan.System<WFAbilitySystem>();
        var shell = entMan.System<WFShellSystem>();
        EntityUid crewman = default;
        EntityUid lurker = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            lurker = entMan.SpawnEntity(Lurker, WFRemadeDirectorTest.At(map, 11, 0));
            Assert.That(shell.IsOpen(lurker), Is.False, "it spawned with its shell open.");
        });

        var spines = abilities.Find<WFSpineAbility>(lurker)!;
        await WFRemadeDirectorTest.RunUntil(pair, () => spines.Volleys > 0, 40);
        await server.WaitAssertion(() =>
        {
            Assert.That(spines.Volleys, Is.GreaterThan(0), "it never fired.");
            Assert.That(spines.SpinesFired, Is.EqualTo(3 * spines.Volleys), "it didn't fire a spine for each tentacle.");
            Assert.That(shell.IsOpen(lurker), Is.True, "it fired with its shell shut.");
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => LimbsTest.Total(pair, crewman) > 0f || entMan.Deleted(crewman), 10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(crewman) || LimbsTest.Total(pair, crewman) > 0f, Is.True, "the spines never reached the crewman.");
            LimbsTest.Hit(pair, lurker, "Blunt", 2, null, crewman);
        });

        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            Assert.That(spines.Curls, Is.GreaterThan(0), "it kept its shell open under fire.");
            Assert.That(shell.IsOpen(lurker), Is.False, "it didn't curl up when hurt.");
            Assert.That(spines.Phase, Is.EqualTo(WFSpinePhase.Closed));
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALurkersTentaclesAreOnlyExposedWhileItsShellIsOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var shell = entMan.System<WFShellSystem>();
        var roster = entMan.System<WFRosterSystem>();

        await server.WaitPost(() =>
        {
            var lurker = entMan.SpawnEntity(Lurker, map.GridCoords);
            var limbs = entMan.GetComponent<WFRemadeLimbsComponent>(lurker);
            var spines = new[] { "tentacle_1", "tentacle_2", "tentacle_3" };

            LimbsTest.Hit(pair, lurker, "Slash", 60, TargetBodyPart.LeftArm);
            Assert.That(limbs.Severed, Is.Empty, "a shut shell let a hit through to a tentacle.");
            Assert.That(roster.Remaining(lurker, spines), Is.EqualTo(3));

            shell.SetOpen(lurker, true);
            LimbsTest.Hit(pair, lurker, "Slash", 60, TargetBodyPart.LeftArm);
            Assert.That(limbs.Severed, Does.Contain("tentacle_1"), "an open shell kept its tentacle.");
            Assert.That(roster.Remaining(lurker, spines), Is.EqualTo(2));

            shell.SetOpen(lurker, false);
            LimbsTest.Hit(pair, lurker, "Slash", 60, TargetBodyPart.Head);
            Assert.That(limbs.Severed, Does.Not.Contain("tentacle_2"), "the shell shut again and the hit still got through.");
            entMan.DeleteEntity(lurker);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryDirectorMobResolvesAndCorpsesCanRiseAsEachWeightedType()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var protos = server.ProtoMan;
        var conversion = entMan.System<WFRemadeConversionSystem>();

        await server.WaitPost(() =>
        {
            var mobs = protos.EnumeratePrototypes<WFRemadeMobPrototype>().ToList();
            foreach (var id in All)
                Assert.That(mobs.Any(mob => mob.Entity.Id == id), Is.True, $"{id} has no wfRemadeMob.");

            foreach (var mob in mobs)
            {
                Assert.That(protos.HasIndex<EntityPrototype>(mob.Entity.Id), Is.True, $"{mob.ID} names {mob.Entity}, which doesn't exist.");
                Assert.That(mob.Cost, Is.GreaterThan(0f), $"{mob.ID} costs nothing.");
                var spawned = entMan.SpawnEntity(mob.Entity, map.GridCoords);
                Assert.That(entMan.HasComponent<WFRemadeLimbsComponent>(spawned), Is.True, $"{mob.ID} has no limbs.");
                entMan.DeleteEntity(spawned);
            }

            var corpse = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            var risen = new HashSet<string>();
            for (var i = 0; i < 4000; i++)
                risen.Add(conversion.Pick(corpse));

            // The ones with conditions (sex, armour, poison, job) are covered by OpenItemsTest.
            foreach (var mob in mobs.Where(mob => mob.ConversionWeight > 0f && !mob.HasConversionConditions))
                Assert.That(risen, Does.Contain(mob.Entity.Id), $"a corpse never rose as {mob.ID} in 4000 tries.");

            foreach (var id in risen)
                Assert.That(protos.HasIndex<EntityPrototype>(id), Is.True, $"a corpse rose as {id}, which doesn't exist.");

            entMan.DeleteEntity(corpse);
        });
        await pair.CleanReturnAsync();
    }
}
