#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Garrison;
using Content.Server._WF.Remade.Monolith;
using Content.Server.NPC.HTN;
using Content.Server.Power.Components;
using Content.Shared._WF.Remade;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

using static Content.IntegrationTests.Tests._WF.Remade.WFRemadeDirectorTest;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>What the Director does with its Remade: vents, playing dead, hearing, flanking, tension, tricks.</summary>
[TestFixture]
public sealed class DirectorBehaviourTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFDirectorTestBullet
  parent: BaseBullet

- type: entity
  id: WFDirectorTestCartridge
  components:
  - type: CartridgeAmmo
    proto: WFDirectorTestBullet
    deleteOnSpawn: true

- type: entity
  id: WFDirectorTestGun
  components:
  - type: Gun
    minAngle: 0
    maxAngle: 0
    selectedMode: SemiAuto
    availableModes:
    - SemiAuto
  - type: BallisticAmmoProvider
    proto: WFDirectorTestCartridge
    capacity: 5
";

    /// <summary>Freezes the Director's own plans so a test can drive one behaviour at a time.</summary>
    private static WFRemadeDirectorComponent Quiesce(IEntityManager entMan, EntityUid scion, IGameTiming timing)
    {
        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        var far = timing.CurTime + TimeSpan.FromHours(1);
        director.Phase = WFRemadeDirectorPhase.Quiet;
        director.PhaseEnd = far;
        director.NextAmbush = far;
        director.NextStalker = far;
        director.NextDread = far;
        return director;
    }

    private static EntityUid SpawnRemade(IEntityManager entMan, EntityUid scion, EntityCoordinates at, string proto = "WFRemadeSlasher")
    {
        var remade = entMan.SpawnEntity(proto, at);
        entMan.System<WFRemadeDirectorSystem>().Enlist(remade, scion);
        entMan.GetComponent<WFRemadeBehaviourComponent>(remade).NextDrift = TimeSpan.FromDays(1);
        return remade;
    }

    [Test]
    public async Task ARemadeTravelsByVentAndArrivesNearThePreyUnseen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var examine = entMan.System<ExamineSystemShared>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, crewman = default, remade = default, start = default, end = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 3, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            start = entMan.SpawnEntity("GasVentPump", At(map, -20, -18));
            entMan.SpawnEntity("GasVentScrubber", At(map, -8, -10));
            entMan.SpawnEntity("GasVentPump", At(map, 8, -1));
            end = entMan.SpawnEntity("GasVentPump", At(map, 19, 6));
            directors.ForgetVents();
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>()).Prey = crewman;

            remade = SpawnRemade(entMan, scion, entMan.GetComponent<TransformComponent>(start).Coordinates);
            Assert.That(directors.TryVentTravel(remade, start, end), Is.True, "the Remade couldn't enter the vent.");
            Assert.That(containers.IsEntityInContainer(remade), Is.True, "a Remade in the vents is still in the world.");
            Assert.That(entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Mode, Is.EqualTo(WFRemadeMode.Hidden));
        });

        await RunUntil(pair, () => !entMan.HasComponent<WFRemadeVentTripComponent>(remade), 60);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadeVentTripComponent>(remade), Is.False, "the Remade never came out of the vent.");
            Assert.That(containers.IsEntityInContainer(remade), Is.False);
            var at = transforms.GetMapCoordinates(remade);
            Assert.That((at.Position - transforms.GetWorldPosition(end)).Length(), Is.LessThan(3f), "it didn't come out at the vent it was sent to.");
            Assert.That(examine.InRangeUnOccluded(crewman, at, 14f), Is.False, "it burst out where the crewman could see it.");
            Assert.That(entMan.GetComponent<WFRemadeDirectorComponent>(scion).Rattles, Is.GreaterThanOrEqualTo(1), "no vent rattled on the way.");
            Assert.That(entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Known, Is.Not.Null, "it came out with no idea where the prey was.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADeadLookingRemadeRisesWhenCrewComeNearOrHitIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var appearance = entMan.System<SharedAppearanceSystem>();
        var mobState = entMan.System<MobStateSystem>();
        var damageable = entMan.System<DamageableSystem>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, crewman = default, walked = default, hit = default;

        MobState Shown(EntityUid uid) => appearance.TryGetData<MobState>(uid, MobStateVisuals.State, out var state) ? state : MobState.Invalid;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, -18, -18));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, 20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            walked = SpawnRemade(entMan, scion, At(map, 0, 0));
            hit = SpawnRemade(entMan, scion, At(map, 10, 10));
            directors.PlayDead(walked);
            directors.PlayDead(hit);

            Assert.That(Shown(walked), Is.EqualTo(MobState.Dead), "it doesn't look dead.");
            Assert.That(mobState.IsAlive(walked), Is.True, "it is really dead, not playing.");
            Assert.That(entMan.GetComponent<HTNComponent>(walked).Enabled, Is.False, "it can still think.");
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadePlayingDeadComponent>(walked), Is.True, "it rose with nobody near.");
            Assert.That(transforms.GetWorldPosition(walked).Length(), Is.LessThan(1f), "it moved while playing dead.");
            transforms.SetCoordinates(crewman, At(map, 2, 1));
        });

        await RunUntil(pair, () => !entMan.HasComponent<WFRemadePlayingDeadComponent>(walked), 5);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<WFRemadePlayingDeadComponent>(walked), Is.False, "it didn't rise when a crewman walked up.");
            Assert.That(Shown(walked), Is.EqualTo(MobState.Alive));
            Assert.That(entMan.GetComponent<HTNComponent>(walked).Enabled, Is.True, "it rose but can't think.");

            Assert.That(entMan.HasComponent<WFRemadePlayingDeadComponent>(hit), Is.True);
            var before = entMan.GetComponent<DamageableComponent>(hit).TotalDamage;
            var blow = new DamageSpecifier();
            blow.DamageDict["Slash"] = 10;
            damageable.TryChangeDamage(hit, blow);
            Assert.That(entMan.GetComponent<DamageableComponent>(hit).TotalDamage, Is.GreaterThan(before), "it took no damage.");
            Assert.That(entMan.HasComponent<WFRemadePlayingDeadComponent>(hit), Is.False, "it stayed down after being hit.");
            Assert.That(Shown(hit), Is.EqualTo(MobState.Alive));
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GunfireDrawsARemadeToTheSourceNotTheCrew()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid scion = default, remade = default, gun = default;
        var shotAt = At(map, 2, -6);

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, -4, 14));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, 20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            remade = SpawnRemade(entMan, scion, At(map, -8, -6));
            // It wanders while it hears nothing; blind it, so a stroll within sight of the crewman can't turn it on them instead of the shot.
            entMan.System<Content.Server.NPC.Systems.NPCSystem>().SetBlackboard(remade, "VisionRadius", 1f);
            entMan.System<Content.Server.NPC.Systems.NPCSystem>().SetBlackboard(remade, "AggroVisionRadius", 1f);
            gun = entMan.SpawnEntity("WFDirectorTestGun", shotAt);
        });
        await pair.RunTicksSync(30);

        // Nothing has made a sound yet, so it has no reason to go anywhere.
        await server.WaitPost(() =>
        {
            Assert.That(entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Known, Is.Null, "it knew where to go before any noise.");
            var firing = entMan.System<SharedGunSystem>();
            firing.AttemptShoot(gun, gun, entMan.GetComponent<GunComponent>(gun), At(map, 20, -6));
        });

        float ToGun() => (transforms.GetWorldPosition(remade) - transforms.GetWorldPosition(gun)).Length();

        await RunUntil(pair, () => entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Known != null, 10);
        await server.WaitAssertion(() =>
        {
            var known = entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Known;
            Assert.That(known, Is.Not.Null, "the shot wasn't heard.");
            var heard = transforms.ToMapCoordinates(known!.Value).Position;
            Assert.That((heard - transforms.GetWorldPosition(gun)).Length(), Is.LessThan(5f), "it was sent somewhere other than the gunfire.");
        });

        await RunUntil(pair, () => ToGun() < 4f, 180);
        await server.WaitAssertion(() =>
        {
            Assert.That(ToGun(), Is.LessThan(4f), $"the Remade didn't reach the gunshot: {Describe(pair, remade)}, known {entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Known}, fight {entMan.GetComponent<HTNComponent>(remade).Blackboard.ContainsKey("Target")}.");
            Assert.That(transforms.GetWorldPosition(remade).Y, Is.LessThan(6f), "the Remade went for the crewman instead of the gunshot.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TalkingDrawsARemadeButWhisperingDoesNot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var chat = entMan.System<Content.Server.Chat.Systems.ChatSystem>();
        EntityUid scion = default, remade = default, speaker = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, -4, 14));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, 20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            remade = SpawnRemade(entMan, scion, At(map, -6, -6));
            entMan.System<Content.Server.NPC.Systems.NPCSystem>().SetBlackboard(remade, "VisionRadius", 1f);
            entMan.System<Content.Server.NPC.Systems.NPCSystem>().SetBlackboard(remade, "AggroVisionRadius", 1f);
            speaker = entMan.SpawnEntity("MobHuman", At(map, 2, -6));
        });
        await pair.RunTicksSync(30);

        // A whisper is the one way to talk it can't hear.
        await server.WaitPost(() => chat.TrySendInGameICMessage(speaker, "keep quiet", Content.Shared.Chat.InGameICChatType.Whisper,
            Content.Shared.Chat.ChatTransmitRange.Normal, ignoreActionBlocker: true));
        await pair.RunTicksSync(90);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Known, Is.Null, "a whisper drew the Remade."));

        await server.WaitPost(() => chat.TrySendInGameICMessage(speaker, "anyone there?", Content.Shared.Chat.InGameICChatType.Speak,
            Content.Shared.Chat.ChatTransmitRange.Normal, ignoreActionBlocker: true));
        await RunUntil(pair, () => entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Known != null, 10);
        await server.WaitAssertion(() =>
        {
            var known = entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Known;
            Assert.That(known, Is.Not.Null, "talking wasn't heard.");
            var heard = transforms.ToMapCoordinates(known!.Value).Position;
            Assert.That((heard - transforms.GetWorldPosition(speaker)).Length(), Is.LessThan(5f), "it was sent somewhere other than the voice.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ARemadeThatHearsNothingOnlyDriftsTowardsTheCrewSlowly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid scion = default, crewman = default, remade = default;
        var start = 0f;

        float Gap() => (transforms.GetWorldPosition(remade) - transforms.GetWorldPosition(crewman)).Length();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, -20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            remade = SpawnRemade(entMan, scion, At(map, -21, 0));
        });

        await pair.RunSeconds(20f);
        await server.WaitAssertion(() =>
        {
            var behaviour = entMan.GetComponent<WFRemadeBehaviourComponent>(remade);
            Assert.That(behaviour.Known, Is.Null, "it sensed a quiet crewman it couldn't hear.");
            behaviour.NextDrift = TimeSpan.Zero;
            start = Gap();
        });

        await RunUntil(pair, () => Gap() < start - 4f, 180);
        await server.WaitAssertion(() =>
        {
            Assert.That(Gap(), Is.LessThan(start - 4f), $"the drift never brought it near the crew: gap {Gap()}, known {(entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Known is {} k ? transforms.ToMapCoordinates(k).Position.ToString() : "none")}, crew {transforms.GetWorldPosition(crewman)}, {Describe(pair, remade)}.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AWaveSplitsAcrossDifferentVentsToFlank()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        var vents = new List<EntityUid>();
        EntityUid scion = default, crewman = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 3, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 60f;
            foreach (var (x, y) in new[] { (20, 0), (-20, 0), (0, 20), (0, -20) })
            {
                vents.Add(entMan.SpawnEntity("GasVentPump", At(map, x, y)));
            }
        });
        await pair.RunSeconds(2f);

        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await server.WaitPost(() =>
        {
            directors.ForgetVents();
            director.Biomass = 500f;
            director.Prey = crewman;
            director.Phase = WFRemadeDirectorPhase.Stalk;
            director.PhaseEnd = server.ResolveDependency<IGameTiming>().CurTime;
        });
        await RunUntil(pair, () => director.Horde.Count >= 4, 10);
        var used = new HashSet<EntityUid>();
        EntityUid NearestVent(EntityUid remade) => vents.MinBy(vent => (transforms.GetWorldPosition(vent) - transforms.GetWorldPosition(remade)).LengthSquared())!;
        await server.WaitPost(() =>
        {
            // Those that walked in are standing at their vent now; the rest are in the ducts and will name the vent they come out of.
            foreach (var remade in director.Horde.Where(remade => !entMan.HasComponent<WFRemadeVentTripComponent>(remade)))
            {
                var nearest = NearestVent(remade);
                if ((transforms.GetWorldPosition(nearest) - transforms.GetWorldPosition(remade)).Length() < 3f)
                    used.Add(nearest);
            }
        });
        // A wave now often comes through the ducts, held out of the world until it bursts out.
        await RunUntil(pair, () => director.Horde.All(remade => !entMan.HasComponent<WFRemadeVentTripComponent>(remade)), 40);

        await server.WaitAssertion(() =>
        {
            Assert.That(director.Horde.Count, Is.GreaterThanOrEqualTo(4), "the wave was too small to split.");
            foreach (var remade in director.Horde)
            {
                if (entMan.GetComponent<WFRemadeBehaviourComponent>(remade).ArrivalVent is { } exit)
                    used.Add(exit);
            }

            Assert.That(used.Count, Is.GreaterThanOrEqualTo(3), $"the wave came out of {used.Count} vent(s), not several.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HighStressDelaysTheNextWaveUntilTheCrewmanCalms()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, crewman = default;

        await server.WaitPost(() =>
        {
            // These predate the garrison, whose guards would frighten the crewman beside the stone and hold the waves back.
            server.CfgMan.SetCVar(WFGarrisonCVars.Enabled, false);
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 3, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
            entMan.SpawnEntity("GasVentPump", At(map, 20, 0));
        });
        await pair.RunSeconds(2f);

        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await server.WaitPost(() =>
        {
            directors.ForgetVents();
            entMan.GetComponent<WFRemadeStressComponent>(crewman).Stress = 100f;
            director.Prey = crewman;
            director.Phase = WFRemadeDirectorPhase.Stalk;
            director.PhaseEnd = server.ResolveDependency<IGameTiming>().CurTime;
        });

        await pair.RunSeconds(20f);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Horde, Is.Empty, "a wave went out at a crewman who was badly shaken.");
            Assert.That(director.Phase, Is.EqualTo(WFRemadeDirectorPhase.Stalk), "the Director gave up the wave instead of holding it.");
            Assert.That(entMan.GetComponent<WFRemadeStressComponent>(crewman).Stress, Is.GreaterThan(director.StressHigh), "the stress bled off too fast for this test.");
            entMan.GetComponent<WFRemadeStressComponent>(crewman).Stress = 0f;
        });

        await RunUntil(pair, () => director.Horde.Count > 0, 15);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Horde, Is.Not.Empty, "the wave never came after the crewman calmed.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NoWaveGoesAtSomeoneWhoJustSurvivedOneBelowRapture()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default, crewman = default;

        await server.WaitPost(() =>
        {
            // These predate the garrison, whose guards would frighten the crewman beside the stone and hold the waves back.
            server.CfgMan.SetCVar(WFGarrisonCVars.Enabled, false);
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 3, 0));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
        });
        await pair.RunSeconds(2f);

        var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
        await server.WaitPost(() =>
        {
            entMan.GetComponent<WFRemadeStressComponent>(crewman).NoWaveUntil = TimeSpan.FromDays(1);
            director.Phase = WFRemadeDirectorPhase.Quiet;
            director.PhaseEnd = TimeSpan.Zero;
            director.Prey = null;
        });

        await pair.RunSeconds(12f);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Phase, Is.EqualTo(WFRemadeDirectorPhase.Quiet), "the Director went for a crewman who had just finished a wave.");
            Assert.That(director.Prey, Is.Null);
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 85f;
        });

        await RunUntil(pair, () => director.Phase != WFRemadeDirectorPhase.Quiet, 15);
        await server.WaitAssertion(() =>
        {
            Assert.That(director.Phase, Is.Not.EqualTo(WFRemadeDirectorPhase.Quiet), "Rapture still spared the crewman.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALockdownBoltsTheDoorsRoundACrewmanAndReleasesThem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var doors = entMan.System<SharedDoorSystem>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, crewman = default, near1 = default, near2 = default, far = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, 20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            near1 = entMan.SpawnEntity("Airlock", At(map, 4, 0));
            near2 = entMan.SpawnEntity("Airlock", At(map, -4, 3));
            far = entMan.SpawnEntity("Airlock", At(map, 0, 17));
            foreach (var door in new[] { near1, near2, far })
            {
                entMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
            }
        });
        await pair.RunTicksSync(60);

        await server.WaitPost(() =>
        {
            var count = directors.Lockdown((scion, entMan.GetComponent<WFRemadeDirectorComponent>(scion)), crewman, 3f);
            Assert.That(count, Is.EqualTo(2), "it didn't bolt exactly the doors round the crewman.");
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorBoltComponent>(near1).BoltsDown, Is.True);
            Assert.That(entMan.GetComponent<DoorBoltComponent>(near2).BoltsDown, Is.True);
            Assert.That(entMan.GetComponent<DoorBoltComponent>(far).BoltsDown, Is.False, "a door out of range was bolted too.");
            Assert.That(doors.CanOpen(near1), Is.False, "a bolted door can be opened.");
            Assert.That(doors.CanOpen(far), Is.True, "the test door isn't powered, so the bolt check proves nothing.");
        });

        await RunUntil(pair, () => !entMan.GetComponent<DoorBoltComponent>(near1).BoltsDown
            && !entMan.GetComponent<DoorBoltComponent>(near2).BoltsDown, 15);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<DoorBoltComponent>(near1).BoltsDown, Is.False, "the lockdown never lifted.");
            Assert.That(entMan.GetComponent<DoorBoltComponent>(near2).BoltsDown, Is.False);
            Assert.That(entMan.HasComponent<WFRemadeLockdownDoorComponent>(near1), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AStalkerFollowsAtADistanceBeforeItCommits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, crewman = default, stalker = default;

        float Gap() => (transforms.GetWorldPosition(stalker) - transforms.GetWorldPosition(crewman)).Length();

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, 20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            stalker = SpawnRemade(entMan, scion, At(map, 15, 0));
            directors.Stalk(stalker, crewman, 25f);
        });

        var closest = float.MaxValue;
        for (var second = 0; second < 18; second++)
        {
            await pair.RunSeconds(1f);
            await server.WaitPost(() => closest = Math.Min(closest, Gap()));
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WFRemadeBehaviourComponent>(stalker).Mode, Is.EqualTo(WFRemadeMode.Stalk), "it committed early.");
            Assert.That(closest, Is.GreaterThan(7f), "the stalker came in close instead of following at a distance.");
        });

        await RunUntil(pair, () => entMan.GetComponent<WFRemadeBehaviourComponent>(stalker).Mode == WFRemadeMode.Hunt, 20);
        await RunUntil(pair, () => Gap() < 3.5f, 90);
        await server.WaitAssertion(() =>
        {
            var behaviour = entMan.GetComponent<WFRemadeBehaviourComponent>(stalker);
            Assert.That(Gap(), Is.LessThan(3.5f), $"the stalker never committed and closed in: mode {behaviour.Mode}, known {behaviour.Known}, awake {entMan.HasComponent<Content.Shared.NPC.ActiveNPCComponent>(stalker)}, htn {entMan.GetComponent<HTNComponent>(stalker).Enabled}.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AHurtRemadeOutOfSightRetreatsToAVentAndComesBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var damageable = entMan.System<DamageableSystem>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, remade = default, hideout = default, nearVent = default;
        var hurt = FixedPoint2.Zero;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, -18, -18));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, -20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            var director = Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            director.RetreatMin = 3f;
            director.RetreatMax = 3f;
            nearVent = entMan.SpawnEntity("GasVentPump", At(map, 20, 18));
            hideout = entMan.SpawnEntity("GasVentPump", At(map, -20, 18));
            directors.ForgetVents();
            remade = SpawnRemade(entMan, scion, At(map, 18, 18));
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var blow = new DamageSpecifier();
            blow.DamageDict["Slash"] = 90;
            damageable.TryChangeDamage(remade, blow, ignoreResistances: true);
            hurt = entMan.GetComponent<DamageableComponent>(remade).TotalDamage;
        });
        var trace = new System.Text.StringBuilder();
        var lastMode = WFRemadeMode.Hidden;
        var polls = 0;
        await RunUntil(pair, () =>
        {
            var mode = entMan.GetComponent<WFRemadeBehaviourComponent>(remade).Mode;
            if (mode != lastMode || polls++ % 12 == 0)
                trace.Append($"t{polls / 4}s {mode}/{Describe(pair, remade)} | ");
            lastMode = mode;
            return mode == WFRemadeMode.Hidden;
        }, 30);
        await server.WaitAssertion(() =>
        {
            var state = entMan.GetComponent<WFRemadeBehaviourComponent>(remade);
            Assert.That(state.Mode, Is.EqualTo(WFRemadeMode.Hidden), $"the hurt Remade never ran for a vent: retreats {state.Retreats}, damage {entMan.GetComponent<DamageableComponent>(remade).TotalDamage}, in a fight {entMan.GetComponent<HTNComponent>(remade).Blackboard.ContainsKey("Target")}, vent tile on the navmesh {entMan.System<Content.Server.NPC.Pathfinding.PathfindingSystem>().GetPoly(entMan.GetComponent<TransformComponent>(nearVent).Coordinates) != null}, {Describe(pair, remade)}. Trace: {trace}");
            Assert.That(containers.IsEntityInContainer(remade), Is.True);
        });

        await RunUntil(pair, () => !containers.IsEntityInContainer(remade), 60);
        await server.WaitAssertion(() =>
        {
            Assert.That(containers.IsEntityInContainer(remade), Is.False, "it never came back.");
            Assert.That((transforms.GetWorldPosition(remade) - transforms.GetWorldPosition(hideout)).Length(), Is.LessThan(4f), "it came back somewhere other than the far vent.");
            Assert.That(entMan.GetComponent<DamageableComponent>(remade).TotalDamage, Is.LessThan(hurt), "it didn't mend while it hid.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TheDirectorLeavesScrawlsAndRunesAndMeddlesWithDoorsAndLights()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var doors = entMan.System<SharedDoorSystem>();
        var examine = entMan.System<ExamineSystemShared>();
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default, crewman = default, door = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            crewman = SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, -20, 20));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            Quiesce(entMan, scion, server.ResolveDependency<IGameTiming>());
            door = entMan.SpawnEntity("Airlock", At(map, 4, 0));
            entMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
            entMan.SpawnEntity("PoweredSmallLight", At(map, 0, 5));
        });
        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(directors.Scrawl(crewman), Is.True, "no scrawl was written.");
            var scrawl = entMan.AllEntities<WFRemadeScrawlComponent>().Single();
            var text = examine.GetExamineText(scrawl.Owner, crewman).ToMarkup();
            Assert.That(text, Does.Contain("It reads"), "examining a scrawl doesn't show the words.");

            Assert.That(directors.Rune(crewman), Is.True, "no rune was drawn.");
            Assert.That(entMan.EntityQuery<MetaDataComponent>().Count(meta => meta.EntityPrototype?.ID == "WFRemadeRune"), Is.EqualTo(1));

            Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Closed));
            Assert.That(directors.Meddle(crewman), Is.True, "no door was meddled with.");
            Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.Not.EqualTo(DoorState.Closed), "the door didn't move.");

            Assert.That(directors.Blowout(crewman), Is.True, "no light blew.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADirectorTickStaysCheapWithTenCrewAndSixteenRemade()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var directors = entMan.System<WFRemadeDirectorSystem>();
        EntityUid scion = default;

        await server.WaitPost(() =>
        {
            LayDeck(pair, map);
            SpawnCrewman(pair, At(map, 0, 0));
            scion = entMan.SpawnEntity("WFMonolithScion", At(map, 3, 3));
            entMan.GetComponent<WFRemadeDirectorComponent>(scion).BuildUp = false;
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 90f;
            entMan.SpawnEntity("GasVentPump", At(map, 20, 0));
            entMan.SpawnEntity("GasVentPump", At(map, -20, 0));
            for (var i = 0; i < 16; i++)
            {
                SpawnRemade(entMan, scion, At(map, -20 + i * 2, 12 + i % 3));
            }
        });
        await pair.RunSeconds(3f);

        await server.WaitAssertion(() =>
        {
            var director = entMan.GetComponent<WFRemadeDirectorComponent>(scion);
            Assert.That(director.Horde.Count, Is.GreaterThanOrEqualTo(16));

            // Only one session can be attached here, so nine more bodies get a bare actor component, which is all the
            // Director's crew gathering looks for. Nothing ticks while this block runs, and they come off again after.
            var extra = new List<EntityUid>();
            for (var i = 0; i < 9; i++)
            {
                var body = entMan.SpawnEntity("MobCarp", At(map, -16 + i * 4, -10 + i % 3 * 3));
                entMan.AddComponent<Robust.Shared.Player.ActorComponent>(body);
                extra.Add(body);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int ticks = 200;
            for (var i = 0; i < ticks; i++)
            {
                director.NextTick = TimeSpan.Zero;
                directors.Update(0.016f);
            }

            sw.Stop();
            var perTick = sw.Elapsed.TotalMilliseconds / ticks;
            foreach (var body in extra)
            {
                entMan.RemoveComponent<Robust.Shared.Player.ActorComponent>(body);
                entMan.DeleteEntity(body);
            }

            TestContext.Out.WriteLine($"Director tick: {perTick:F3} ms with 10 crew and {director.Horde.Count} Remade.");
            Assert.That(perTick, Is.LessThan(2.0), $"a Director tick took {perTick:F2} ms with 10 crew and {director.Horde.Count} Remade.");
        });
        await pair.CleanReturnAsync();
    }
}
