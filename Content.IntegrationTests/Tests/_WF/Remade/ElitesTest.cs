#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client.DamageState;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Abilities;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Elites;
using Content.Server._WF.Remade.Limbs;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Projectiles;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Projectiles;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The elite Remade: each spawns and hunts, and what makes each of them what it is does what it says.</summary>
[TestFixture]
public sealed class ElitesTest
{
    private const string Brute = "WFRemadeBrute";
    private const string Twitcher = "WFRemadeTwitcher";
    private const string Oracle = "WFRemadeTwitcherOracle";
    private const string Hunter = "WFRemadeHunter";
    private const string Divider = "WFRemadeDivider";
    private const string Tripod = "WFRemadeTripod";
    private const string Undying = "WFRemadeUndying";
    private const string Infector = "WFRemadeInfectorEnhanced";
    private const string Arm = "WFRemadeDividerArm";
    private const string Leg = "WFRemadeDividerLeg";
    private const string Head = "WFRemadeDividerHead";

    private static readonly string[] Limbed = { Brute, Twitcher, Oracle, Hunter, Divider, Tripod, Undying, Infector };
    private static readonly string[] Parts = { Arm, Leg, Head };

    private static float Distance(SharedTransformSystem transforms, EntityUid a, EntityUid b)
    {
        return (transforms.GetWorldPosition(a) - transforms.GetWorldPosition(b)).Length();
    }

    /// <summary>Damage that ignores armour and the limbs, to put a mob down for certain.</summary>
    private static void Pound(TestPair pair, EntityUid uid, string type, float amount)
    {
        var damage = new DamageSpecifier(pair.Server.ProtoMan.Index<DamageTypePrototype>(type), FixedPoint2.New(amount));
        pair.Server.EntMan.System<DamageableSystem>().TryChangeDamage(uid, damage, ignoreResistances: true, interruptsDoAfters: false, canSever: false);
    }

    [Test]
    public async Task EveryEliteSpawnsWithItsPartsAndItsSpriteStatesExist()
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
            var x = -16;
            foreach (var id in Limbed.Concat(Parts))
            {
                mobs[id] = entMan.SpawnEntity(id, WFRemadeDirectorTest.At(map, x, 6));
                x += 3;
            }
        });
        await pair.RunTicksSync(15);

        await server.WaitAssertion(() =>
        {
            foreach (var (id, uid) in mobs)
            {
                // The infector has its work for its mind, not abilities.
                Assert.That(entMan.HasComponent<WFRemadeAbilitiesComponent>(uid), Is.EqualTo(id != Infector), $"{id} has no abilities.");
                Assert.That(entMan.HasComponent<WFRemadeVoiceComponent>(uid), Is.True, $"{id} has no voice.");
                Assert.That(entMan.System<MobStateSystem>().IsAlive(uid), Is.True, $"{id} didn't spawn alive.");
                if (Parts.Contains(id))
                    continue;

                var limbs = entMan.GetComponent<WFRemadeLimbsComponent>(uid);
                Assert.That(limbs.Limbs.Any(limb => !limb.Severable), Is.True, $"{id} has no torso.");
                Assert.That(limbs.Limbs.Count(limb => limb.Severable), Is.GreaterThanOrEqualTo(3), $"{id} has hardly any limbs.");
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
                var ent = new Entity<SpriteComponent?>(client, sprite);

                if (Parts.Contains(id))
                {
                    var visuals = pair.Client.EntMan.GetComponent<DamageStateVisualsComponent>(client);
                    Assert.That(sprites.LayerMapTryGet(ent, DamageStateVisualLayers.Base, out var layer, false), Is.True, $"{id}: no base layer.");
                    var sheet = sprite.BaseRSI;
                    Assert.That(sheet, Is.Not.Null, $"{id}: no sprite sheet.");
                    var states = new[] { sprites.LayerGetRsiState(ent, layer).Name ?? string.Empty }
                        .Concat(visuals.States[MobState.Alive].Values)
                        .Concat(visuals.States[MobState.Dead].Values);
                    foreach (var state in states)
                    {
                        Assert.That(sheet!.TryGetState(state, out _), Is.True, $"{id}: the sheet has no state '{state}'.");
                    }

                    continue;
                }

                var limbs = pair.Client.EntMan.GetComponent<WFRemadeLimbsComponent>(client);
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
    public async Task EveryEliteWalksToACrewmanAndHurtsIt()
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

        foreach (var id in Limbed.Append(Arm).Append(Leg).Append(Head))
        {
            EntityUid crewman = default;
            EntityUid mob = default;
            await server.WaitPost(() =>
            {
                crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
                mob = entMan.SpawnEntity(id, WFRemadeDirectorTest.At(map, 9, 0));
            });

            await WFRemadeDirectorTest.RunUntil(pair, () => LimbsTest.Total(pair, crewman) > 0f || entMan.Deleted(crewman), 60);
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
    public async Task ABruteTakesMuchLessFromTheFrontThanFromBehind()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            var brute = entMan.SpawnEntity(Brute, WFRemadeDirectorTest.At(map, 0, 0));
            // Facing south, which is the way rotation zero faces.
            transforms.SetWorldRotation(brute, Angle.Zero);

            var front = entMan.SpawnEntity("MobCarp", WFRemadeDirectorTest.At(map, 0, -5));
            var back = entMan.SpawnEntity("MobCarp", WFRemadeDirectorTest.At(map, 0, 5));
            var side = entMan.SpawnEntity("MobCarp", WFRemadeDirectorTest.At(map, 5, 0));

            float Blow(EntityUid origin)
            {
                var before = LimbsTest.Total(pair, brute);
                LimbsTest.Hit(pair, brute, "Piercing", 30, TargetBodyPart.Torso, origin);
                var taken = LimbsTest.Total(pair, brute) - before;
                entMan.System<DamageableSystem>().SetAllDamage(brute, entMan.GetComponent<DamageableComponent>(brute), 0);
                return taken;
            }

            var fromFront = Blow(front);
            var fromBack = Blow(back);
            var fromSide = Blow(side);
            Assert.That(fromFront, Is.GreaterThan(0f), "a blow from the front did nothing at all.");
            Assert.That(fromFront * 3f, Is.LessThan(fromBack), $"front {fromFront}, back {fromBack}: the front isn't armoured enough.");
            Assert.That(fromSide, Is.GreaterThan(fromFront).And.LessThan(fromBack), $"front {fromFront}, side {fromSide}, back {fromBack}.");

            // A brute that turns round presents its armour the other way.
            transforms.SetWorldRotation(brute, Angle.FromDegrees(180));
            var turned = Blow(front);
            Assert.That(turned, Is.EqualTo(fromBack).Within(0.01f), "turning round didn't turn the armour.");

            foreach (var uid in new[] { brute, front, back, side })
                entMan.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASmashBreaksAWindowAndADoorButNotAWall()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var elites = entMan.System<WFEliteSystem>();
        EntityUid brute = default, window = default, door = default, wall = default, behind = default;
        var hits = new List<int>();

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            brute = entMan.SpawnEntity(Brute, WFRemadeDirectorTest.At(map, 0, 0));
            window = entMan.SpawnEntity("Window", WFRemadeDirectorTest.At(map, 1, 0));
            door = entMan.SpawnEntity("Airlock", WFRemadeDirectorTest.At(map, 0, 1));
            wall = entMan.SpawnEntity("WallSolid", WFRemadeDirectorTest.At(map, -1, 0));
            behind = entMan.SpawnEntity("Window", WFRemadeDirectorTest.At(map, 0, -1));

            // East is where the window is; the door, the wall and the window behind are outside a narrow arc.
            hits.Add(elites.Smash(brute, new Vector2(1, 0), 1.5f, 30f, 600f));
            hits.Add(elites.Smash(brute, new Vector2(0, 1), 1.5f, 30f, 600f));
            hits.Add(elites.Smash(brute, new Vector2(-1, 0), 1.5f, 30f, 600f));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(hits[0], Is.EqualTo(1), "a swing east should have hit exactly the window.");
            Assert.That(entMan.Deleted(window), Is.True, "the window survived.");
            Assert.That(hits[1], Is.GreaterThanOrEqualTo(1), "a swing at the door hit nothing.");
            Assert.That(entMan.Deleted(door) || LimbsTest.Total(pair, door) > 0f, Is.True, "the door took no damage.");
            Assert.That(hits[2], Is.Zero, "it hit a wall.");
            Assert.That(entMan.Deleted(wall), Is.False, "the wall is gone.");
            Assert.That(entMan.Deleted(behind), Is.False, "it swung at what was behind it.");
            Assert.That(elites.IsBreakable(brute), Is.False, "a living thing counts as breakable.");
            entMan.DeleteEntity(brute);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABruteChargeBreaksThroughAWindowLineAndHitsSomeoneOnTheWay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid crewman = default;
        EntityUid brute = default;
        var windows = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            // A wall of glass across the whole deck: there's no way round, so it has to go through.
            for (var y = -21; y <= 21; y++)
                windows.Add(entMan.SpawnEntity("Window", WFRemadeDirectorTest.At(map, 4, y)));

            brute = entMan.SpawnEntity(Brute, WFRemadeDirectorTest.At(map, 13, 0));
        });

        var ram = entMan.System<WFAbilitySystem>().Find<WFRamAbility>(brute)!;
        await WFRemadeDirectorTest.RunUntil(pair, () => ram.Smashed > 0, 40);
        var broken = 0;
        await server.WaitPost(() => broken = windows.Count(window => entMan.Deleted(window)));
        await WFRemadeDirectorTest.RunUntil(pair, () => LimbsTest.Total(pair, crewman) > 0f || entMan.Deleted(crewman), 40);

        await server.WaitAssertion(() =>
        {
            Assert.That(ram.Charges, Is.GreaterThan(0), "it never charged.");
            Assert.That(ram.Smashed, Is.GreaterThan(0), "the charge broke nothing.");
            Assert.That(broken, Is.GreaterThan(0), "no window went.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABruteChargeBuildsSpeedButStaysUnderACrewmansRun()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitPost(() =>
        {
            var brute = entMan.SpawnEntity(Brute, WFRemadeDirectorTest.At(map, 0, 0));
            var human = entMan.GetComponent<MovementSpeedModifierComponent>(entMan.SpawnEntity("MobHuman", WFRemadeDirectorTest.At(map, 5, 5)));
            var speed = entMan.GetComponent<MovementSpeedModifierComponent>(brute);
            var ram = entMan.System<WFAbilitySystem>().Find<WFRamAbility>(brute)!;
            var top = speed.BaseSprintSpeed * ram.TopSpeed;
            Assert.That(top, Is.LessThan(human.BaseSprintSpeed), $"a charging brute ({top}) outruns a running crewman ({human.BaseSprintSpeed}).");
            Assert.That(speed.BaseSprintSpeed, Is.LessThan(human.BaseWalkSpeed + 0.01f), "a brute walks faster than a crewman.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ABruteSlamHitsEveryoneRoundIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid crewman = default;
        EntityUid other = default;
        EntityUid brute = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 2, 0));
            other = entMan.SpawnEntity("MobHuman", WFRemadeDirectorTest.At(map, -2, 0));
            brute = entMan.SpawnEntity(Brute, WFRemadeDirectorTest.At(map, 0, 0));
        });

        var slam = entMan.System<WFAbilitySystem>().Find<WFSwingAbility>(brute)!;
        await WFRemadeDirectorTest.RunUntil(pair, () => slam.Swings > 0, 30);
        await pair.RunSeconds(0.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(slam.Swings, Is.GreaterThan(0), "it never slammed.");
            Assert.That(slam.Hits, Is.GreaterThanOrEqualTo(2), "the slam missed someone standing beside it.");
            Assert.That(LimbsTest.Total(pair, other), Is.GreaterThan(0f), "the man on the far side wasn't hurt.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ATwitcherSlipsBulletsUntilItsPoolRunsDryAndTheNextOneHits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var projectiles = entMan.System<ProjectileSystem>();
        var damageable = entMan.System<DamageableSystem>();

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            var shooter = entMan.SpawnEntity("MobCarp", WFRemadeDirectorTest.At(map, 8, 0));
            var twitcher = entMan.SpawnEntity(Twitcher, WFRemadeDirectorTest.At(map, 0, 0));
            var pool = entMan.GetComponent<WFDodgePoolComponent>(twitcher);

            void Fire()
            {
                var shot = entMan.SpawnEntity("WFRemadeSpitSnap", WFRemadeDirectorTest.At(map, 0, 0));
                var projectile = entMan.GetComponent<ProjectileComponent>(shot);
                projectile.Shooter = shooter;
                projectile.Damage = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Piercing"), FixedPoint2.New(30));
                projectiles.ProjectileCollide((shot, projectile, entMan.GetComponent<PhysicsComponent>(shot)), twitcher);
            }

            // A full pool slips the first bullet for certain.
            var before = LimbsTest.Total(pair, twitcher);
            Fire();
            Assert.That(pool.Dodged, Is.EqualTo(1), "a full pool didn't slip a bullet.");
            Assert.That(LimbsTest.Total(pair, twitcher), Is.EqualTo(before), "a slipped bullet still hurt.");

            // Slipping costs some of the pool, a bit at a time.
            for (var i = 0; i < 300 && pool.Pool >= pool.Max; i++)
                Fire();

            Assert.That(pool.Pool, Is.LessThan(pool.Max), "dodging never drained the pool.");
            Assert.That(pool.Pool, Is.GreaterThan(pool.Max - 10 * pool.Usage), "the pool emptied far too fast.");

            // An empty pool slips nothing.
            pool.Pool = 0f;
            var dodged = pool.Dodged;
            before = LimbsTest.Total(pair, twitcher);
            Fire();
            Assert.That(pool.Dodged, Is.EqualTo(dodged), "an empty pool slipped a bullet.");
            Assert.That(LimbsTest.Total(pair, twitcher), Is.GreaterThan(before), "the bullet didn't hurt it.");

            pool.Pool = 50f;
        });

        await pair.RunSeconds(10f);

        await server.WaitAssertion(() =>
        {
            foreach (var twitcher in entMan.EntityQuery<WFDodgePoolComponent>())
            {
                Assert.That(twitcher.Pool, Is.EqualTo(56f).Within(1.5f), "the pool didn't fill back.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ATwitcherBlinksAndAWallStopsIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var elites = entMan.System<WFEliteSystem>();

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            var twitcher = entMan.SpawnEntity(Twitcher, WFRemadeDirectorTest.At(map, 0, 0));
            var start = transforms.GetWorldPosition(twitcher);
            Assert.That(elites.Blink(twitcher, new Vector2(1, 0), 3f), Is.True, "it couldn't blink across open floor.");
            Assert.That((transforms.GetWorldPosition(twitcher) - start).Length(), Is.EqualTo(3f).Within(0.05f));

            entMan.SpawnEntity("WallSolid", WFRemadeDirectorTest.At(map, 5, 0));
            var here = transforms.GetWorldPosition(twitcher);
            Assert.That(elites.Blink(twitcher, new Vector2(1, 0), 3f), Is.False, "it blinked into a wall.");
            Assert.That(transforms.GetWorldPosition(twitcher), Is.EqualTo(here));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AHunterGetsBackUpAfterDyingUnlessBurnedOrDismemberedOrKilledAgainAtOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mobState = entMan.System<MobStateSystem>();
        var limbs = entMan.System<WFRemadeLimbsSystem>();
        EntityUid plain = default, burned = default, headless = default, ripped = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            plain = entMan.SpawnEntity(Hunter, WFRemadeDirectorTest.At(map, -9, 0));
            burned = entMan.SpawnEntity(Hunter, WFRemadeDirectorTest.At(map, -3, 0));
            headless = entMan.SpawnEntity(Hunter, WFRemadeDirectorTest.At(map, 3, 0));
            ripped = entMan.SpawnEntity(Hunter, WFRemadeDirectorTest.At(map, 9, 0));

            // One limb is already gone from the plain one, which has to grow back.
            Assert.That(limbs.TrySever(plain, "l_arm"), Is.True);
            Pound(pair, plain, "Blunt", 400);

            Pound(pair, burned, "Heat", 200);
            Pound(pair, burned, "Blunt", 200);

            Assert.That(limbs.TrySever(headless, "head"), Is.True);
            Pound(pair, headless, "Blunt", 400);

            foreach (var limb in new[] { "l_arm", "r_arm", "l_leg", "r_leg" })
                Assert.That(limbs.TrySever(ripped, limb), Is.True);
            Pound(pair, ripped, "Blunt", 400);

            foreach (var uid in new[] { plain, burned, headless, ripped })
                Assert.That(mobState.IsDead(uid), Is.True, "a hunter didn't go down.");
        });

        await pair.RunSeconds(8f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(plain), Is.True, "the hunter got up before its time.");
        });

        await pair.RunSeconds(10f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsAlive(plain), Is.True, "the hunter never got back up.");
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(plain).Severed, Is.Empty, "it didn't grow its arm back.");
            Assert.That(LimbsTest.Total(pair, plain), Is.LessThan(200f), "it got up with hardly any health.");
            Assert.That(entMan.GetComponent<WFRisesComponent>(plain).Rises, Is.EqualTo(1));

            Assert.That(mobState.IsDead(burned), Is.True, "the burned hunter got up.");
            Assert.That(mobState.IsDead(headless), Is.True, "the hunter with no head got up.");
            Assert.That(mobState.IsDead(ripped), Is.True, "the dismembered hunter got up.");
            Assert.That(entMan.GetComponent<WFRisesComponent>(burned).Final, Is.True);

            // Killed again within two minutes of rising, it stays down.
            Pound(pair, plain, "Blunt", 400);
            Assert.That(mobState.IsDead(plain), Is.True);
        });

        await pair.RunSeconds(30f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(plain), Is.True, "a hunter killed again at once got up again.");
            Assert.That(entMan.GetComponent<WFRisesComponent>(plain).Final, Is.True);
            foreach (var uid in new[] { plain, burned, headless, ripped })
                entMan.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AHunterTauntHardensItsFriendsAndEndsWhenNobodyIsLeft()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid hunter = default;
        EntityUid slasher = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            hunter = entMan.SpawnEntity(Hunter, WFRemadeDirectorTest.At(map, 10, 0));
            slasher = entMan.SpawnEntity("WFRemadeSlasher", WFRemadeDirectorTest.At(map, 12, 3));
            entMan.EnsureComponent<WFRemadeComponent>(slasher);
            entMan.EnsureComponent<WFRemadeComponent>(hunter);
        });

        var taunt = entMan.System<WFAbilitySystem>().Find<WFTauntAbility>(hunter)!;
        var buffed = false;
        await WFRemadeDirectorTest.RunUntil(pair, () => buffed |= entMan.HasComponent<WFTauntBuffComponent>(slasher), 30);

        await server.WaitAssertion(() =>
        {
            Assert.That(taunt.Taunts, Is.GreaterThan(0), "the hunter never taunted.");
            Assert.That(buffed, Is.True, "its friend never got the buff.");
            Assert.That(entMan.GetComponent<WFTauntBuffComponent>(slasher).Multiplier, Is.EqualTo(0.7f).Within(0.001f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADividerComesApartIntoMobsThatHuntAndHurt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid crewman = default;
        EntityUid divider = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            divider = entMan.SpawnEntity(Divider, WFRemadeDirectorTest.At(map, 8, 0));
        });

        // Its first arm comes off on its own: one part for one limb.
        await server.WaitPost(() =>
        {
            Assert.That(entMan.System<WFRemadeLimbsSystem>().TrySever(divider, "l_arm"), Is.True);
            var released = entMan.GetComponent<WFDividerComponent>(divider).Released;
            Assert.That(released, Has.Count.EqualTo(1), "a limb came off and no part crawled away.");
            Assert.That(entMan.GetComponent<MetaDataComponent>(released[0]).EntityPrototype?.ID, Is.EqualTo(Arm));
        });

        await server.WaitPost(() => Pound(pair, divider, "Slash", 350));
        await server.WaitAssertion(() =>
        {
            var released = entMan.GetComponent<WFDividerComponent>(divider).Released;
            var ids = released.Select(uid => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID).ToList();
            Assert.That(ids.Count(id => id == Arm), Is.EqualTo(2), "a divider's two arms didn't both come away.");
            Assert.That(ids.Count(id => id == Leg), Is.EqualTo(2));
            Assert.That(ids.Count(id => id == Head), Is.EqualTo(1));
            foreach (var part in released)
                Assert.That(entMan.System<MobStateSystem>().IsAlive(part), Is.True, "a part was born dead.");
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => LimbsTest.Total(pair, crewman) > 0f || entMan.Deleted(crewman), 60);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(crewman) || LimbsTest.Total(pair, crewman) > 0f, Is.True, "the parts never reached the crewman.");
            foreach (var part in entMan.GetComponent<WFDividerComponent>(divider).Released.Where(part => !entMan.Deleted(part)))
                entMan.DeleteEntity(part);

            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADividerArmLeapsOntoACrewmanAndClingsToHim()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid crewman = default;
        EntityUid arm = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            arm = entMan.SpawnEntity(Arm, WFRemadeDirectorTest.At(map, 4, 0));
        });

        var latch = entMan.System<WFAbilitySystem>().Find<WFLatchAbility>(arm)!;
        await WFRemadeDirectorTest.RunUntil(pair, () => latch.Bites >= 2, 40);

        await server.WaitAssertion(() =>
        {
            Assert.That(latch.Latches, Is.GreaterThan(0), "the arm never latched on.");
            Assert.That(latch.Bites, Is.GreaterThanOrEqualTo(2), "the arm clung and didn't bite.");
            Assert.That(latch.Victim, Is.EqualTo(crewman));
            Assert.That(Distance(transforms, arm, crewman), Is.LessThan(0.3f), "the arm let go of him.");
            Assert.That(LimbsTest.Total(pair, crewman), Is.GreaterThan(0f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ATripodTongueHaulsACrewmanInAndStabsHim()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid crewman = default;
        EntityUid tripod = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            tripod = entMan.SpawnEntity(Tripod, WFRemadeDirectorTest.At(map, 9, 0));
        });

        var tongue = entMan.System<WFAbilitySystem>().Find<WFTongueAbility>(tripod)!;
        var farthest = 0f;
        var closestAfter = float.MaxValue;
        await WFRemadeDirectorTest.RunUntil(pair, () =>
        {
            var distance = Distance(transforms, tripod, crewman);
            if (tongue.Hauls == 0)
                farthest = Math.Max(farthest, distance);
            else
                closestAfter = Math.Min(closestAfter, distance);

            return tongue.Stabbed;
        }, 40);

        await server.WaitAssertion(() =>
        {
            Assert.That(tongue.Hauls, Is.GreaterThan(0), "the tongue never went out.");
            Assert.That(farthest, Is.GreaterThan(3f), "it hauled someone who was already beside it.");
            Assert.That(closestAfter, Is.LessThan(2.5f), "the crewman wasn't dragged in.");
            Assert.That(tongue.Stabbed, Is.True, "the haul ended without a stab.");
            Assert.That(LimbsTest.Total(pair, crewman), Is.GreaterThan(0f), "the stab did nothing.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ATripodWithNoTongueCannotGrab()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid tripod = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            tripod = entMan.SpawnEntity(Tripod, WFRemadeDirectorTest.At(map, 8, 0));
            Assert.That(entMan.System<WFRemadeLimbsSystem>().TrySever(tripod, "head"), Is.True);
        });

        var tongue = entMan.System<WFAbilitySystem>().Find<WFTongueAbility>(tripod)!;
        await pair.RunSeconds(1f);
        await WFRemadeDirectorTest.RunUntil(pair, () => false, 20);

        await server.WaitAssertion(() =>
        {
            Assert.That(tongue.Hauls, Is.Zero, "it used a tongue it doesn't have.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheUndyingIsHeardByEveryoneOnTheGridSurvivesAnythingAndFormsAgain()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mobState = entMan.System<MobStateSystem>();
        var limbs = entMan.System<WFRemadeLimbsSystem>();
        EntityUid crewman = default;
        EntityUid undying = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            crewman = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            undying = entMan.SpawnEntity(Undying, WFRemadeDirectorTest.At(map, 12, 0));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFUndyingComponent>(undying).HeardBy, Does.Contain(crewman), "the crewman on its grid didn't hear it arrive.");
            var speed = entMan.GetComponent<MovementSpeedModifierComponent>(undying);
            Assert.That(speed.BaseSprintSpeed, Is.EqualTo(speed.BaseWalkSpeed), "it runs.");
            Assert.That(speed.BaseSprintSpeed, Is.LessThan(4f), "it outruns a crewman.");

            // Damage that would kill anything folds it up, and a minute later it's whole.
            Pound(pair, undying, "Blunt", 5000);
            Assert.That(mobState.IsDead(undying), Is.True, "lethal damage didn't put it down.");
            Assert.That(entMan.GetComponent<WFRisesComponent>(undying).Final, Is.False);
        });

        await pair.RunSeconds(40f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsDead(undying), Is.True, "it got up early.");
        });

        await pair.RunSeconds(25f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsAlive(undying), Is.True, "it never came back.");
            Assert.That(LimbsTest.Total(pair, undying), Is.LessThan(5f), "it came back hurt.");

            // Take every limb off and it collapses, then forms again with all of them.
            foreach (var limb in entMan.GetComponent<WFRemadeLimbsComponent>(undying).Limbs.Where(limb => limb.Severable).Select(limb => limb.Id).ToList())
                limbs.TrySever(undying, limb);

            Assert.That(mobState.IsDead(undying), Is.True, "losing every limb didn't bring it down.");
        });

        await pair.RunSeconds(65f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsAlive(undying), Is.True, "it stayed down after losing every limb.");
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(undying).Severed, Is.Empty, "it re-formed without all its limbs.");

            // One limb off and it grows back on its own within seconds.
            Assert.That(limbs.TrySever(undying, "r_arm"), Is.True);
        });

        await pair.RunSeconds(12f);
        await server.WaitAssertion(() =>
        {
            Assert.That(mobState.IsAlive(undying), Is.True);
            Assert.That(entMan.GetComponent<WFRemadeLimbsComponent>(undying).Severed, Is.Empty, "a lost arm never grew back.");
            entMan.DeleteEntity(undying);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheDirectorKeepsToEachEliteLimitAndItsStage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var director = entMan.System<WFRemadeDirectorSystem>();
        var buy = typeof(WFRemadeDirectorSystem).GetMethod("Buy", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var bought = new Dictionary<string, int>();
        EntityUid scion = default;
        EntityUid prey = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            prey = WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, 0, 0));
            // Too far from the crewman for its own Director to wake and buy anything.
            scion = entMan.SpawnEntity("WFMonolithScion", WFRemadeDirectorTest.At(map, 150, 150));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            var corpse = entMan.SpawnEntity("MobHuman", WFRemadeDirectorTest.At(map, 3, 3));
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
        });

        // Harvest: twitchers and dividers, and none of the rapture elites.
        await server.WaitPost(() => entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 60f);
        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
            var comp = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Harvest), "the Monolith isn't at Harvest.");
            for (var i = 0; i < 1500; i++)
            {
                comp.Biomass = 100000f;
                var id = (EntProtoId?) buy.Invoke(director, new object[] { new Entity<WFRemadeDirectorComponent, WFMonolithComponent>(scion, comp, monolith), prey, false });
                if (id != null)
                    bought[id.Value.Id] = bought.GetValueOrDefault(id.Value.Id) + 1;
            }

            Assert.That(bought.GetValueOrDefault(Twitcher), Is.GreaterThan(0), "no twitcher at Harvest.");
            Assert.That(bought.GetValueOrDefault(Divider), Is.GreaterThan(0), "no divider at Harvest.");
            foreach (var late in new[] { Oracle, Brute, Hunter, Tripod, Undying, Infector })
                Assert.That(bought.GetValueOrDefault(late), Is.Zero, $"{late} came before Rapture.");
        });

        // Rapture: they all come, but each stops at its limit while it's alive.
        await server.WaitPost(() => entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 85f);
        await pair.RunSeconds(1f);
        var alive = new Dictionary<string, int>();
        await server.WaitAssertion(() =>
        {
            var monolith = entMan.GetComponent<WFMonolithComponent>(scion);
            var comp = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            Assert.That(monolith.Stage, Is.EqualTo(WFMonolithStage.Rapture), "the Monolith isn't at Rapture.");
            var limits = new Dictionary<string, int> { [Brute] = 2, [Hunter] = 1, [Tripod] = 1, [Undying] = 1, [Oracle] = 1, [Infector] = 1, ["WFRemadeInfector"] = 1 };
            bought.Clear();
            for (var i = 0; i < 4000; i++)
            {
                comp.Biomass = 100000f;
                var id = (EntProtoId?) buy.Invoke(director, new object[] { new Entity<WFRemadeDirectorComponent, WFMonolithComponent>(scion, comp, monolith), prey, true });
                if (id == null)
                    continue;

                bought[id.Value.Id] = bought.GetValueOrDefault(id.Value.Id) + 1;
                if (limits.ContainsKey(id.Value.Id))
                {
                    var mob = entMan.SpawnEntity(id.Value, WFRemadeDirectorTest.At(map, 18, -18));
                    director.Enlist(mob, scion);
                    alive[id.Value.Id] = alive.GetValueOrDefault(id.Value.Id) + 1;
                }
            }

            foreach (var (id, limit) in limits)
            {
                Assert.That(alive.GetValueOrDefault(id), Is.EqualTo(limit), $"{id}: the Director kept {alive.GetValueOrDefault(id)} alive, not {limit}.");
            }
        });

        await server.WaitPost(() =>
        {
            var remade = new List<EntityUid>();
            var query = entMan.AllEntityQueryEnumerator<WFRemadeComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                remade.Add(uid);
            }

            foreach (var uid in remade)
                entMan.DeleteEntity(uid);
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryEliteIsPricedGatedAndConvertedAsPlanned()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        // DS13's biomass costs, and when each is first sent. Only the divider is turned from a corpse.
        var expected = new Dictionary<string, (float Cost, WFMonolithStage Stage)>
        {
            [Twitcher] = (150f, WFMonolithStage.Harvest),
            [Oracle] = (210f, WFMonolithStage.Rapture),
            [Divider] = (145f, WFMonolithStage.Harvest),
            [Brute] = (360f, WFMonolithStage.Rapture),
            [Hunter] = (400f, WFMonolithStage.Rapture),
            [Tripod] = (360f, WFMonolithStage.Rapture),
            [Undying] = (800f, WFMonolithStage.Rapture),
            [Infector] = (175f, WFMonolithStage.Rapture),
        };

        var proto = pair.Server.ProtoMan;
        foreach (var (id, (cost, stage)) in expected)
        {
            var mob = proto.Index<WFRemadeMobPrototype>(id);
            Assert.That(mob.Cost, Is.EqualTo(cost), $"{id}: the cost isn't DS13's.");
            Assert.That(mob.MinStage, Is.EqualTo(stage), $"{id}: wrong stage.");
            Assert.That(mob.SpawnWeight, Is.GreaterThan(0f).And.LessThanOrEqualTo(2.5f), $"{id}: an elite should be rare.");
            Assert.That(mob.ConversionWeight > 0f, Is.EqualTo(id == Divider), $"{id}: only the divider is turned from a corpse.");
        }

        Assert.That(proto.Index<WFRemadeMobPrototype>(Undying).MaxAlive, Is.EqualTo(1));
        Assert.That(proto.Index<WFRemadeMobPrototype>(Brute).MaxAlive, Is.EqualTo(2));
        Assert.That(proto.Index<WFRemadeMobPrototype>(Infector).NeedsCorpses, Is.True);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnEnhancedInfectorTurnsACorpseInLessTime()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        EntityUid corpse = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            WFRemadeDirectorTest.LayDeck(pair, map);
            corpse = entMan.SpawnEntity("MobHuman", WFRemadeDirectorTest.At(map, 4, 0));
            entMan.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            // NPCs sleep with no living player near, and this one is too far off to draw the Infector.
            WFRemadeDirectorTest.SpawnCrewman(pair, WFRemadeDirectorTest.At(map, -20, -20));
            entMan.SpawnEntity(Infector, WFRemadeDirectorTest.At(map, 0, 0));
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.HasComponent<WFRemadeConvertingComponent>(corpse), 40);
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadeConvertingComponent>(corpse), Is.True, "the enhanced infector never reached the corpse.");
            var left = (entMan.GetComponent<WFRemadeConvertingComponent>(corpse).EndsAt - timing.CurTime).TotalSeconds;
            Assert.That(left, Is.LessThan(8d), "the corpse is turning no faster than a plain infector's 12 seconds.");
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(corpse), 20);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(corpse), Is.True, "the corpse never turned.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }
}
