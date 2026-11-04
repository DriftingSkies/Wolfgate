#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Abilities;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Signals;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.NPC.HTN;
using Content.Server.Power.Components;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Signals;
using Content.Shared.Actions;
using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>The Signals: ghosts joining a Monolith, the leash, driving a Remade, the tricks and being let go.</summary>
[TestFixture]
public sealed class SignalsTest
{
    private static EntityCoordinates At(TestMapData map, float x, float y) => WFRemadeDirectorTest.At(map, x, y);

    /// <summary>A Scion set to a resonance. The Director stays unless asked to go, so a test sees only the Signals.</summary>
    private static Entity<WFMonolithComponent> SpawnScion(IEntityManager entMan, EntityCoordinates at, float resonance, bool keepDirector = false)
    {
        var uid = entMan.SpawnEntity("WFMonolithScion", at);
        entMan.GetComponent<WFRemadeDirectorComponent>(uid).BuildUp = false;
        if (!keepDirector)
            entMan.RemoveComponent<WFRemadeDirectorComponent>(uid);

        var comp = entMan.GetComponent<WFMonolithComponent>(uid);
        comp.Resonance = resonance;
        entMan.System<WFMonolithSystem>().AddResonance((uid, comp), 0f);
        return (uid, comp);
    }

    /// <summary>Puts the test player in a ghost, the way a dead player would be.</summary>
    private static EntityUid MakeGhost(TestPair pair, TestMapData map)
    {
        var ghost = pair.Server.EntMan.SpawnEntity("MobObserver", At(map, 8, 8));
        pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, ghost);
        return ghost;
    }

    /// <summary>The ghost role a Monolith offers, if it is on offer.</summary>
    private static GhostRoleComponent? OfferedRole(IEntityManager entMan, EntityUid scion)
    {
        if (!entMan.TryGetComponent(scion, out WFSignalStoneComponent? held) || held.Gate is not { } gate
            || entMan.GetComponent<MetaDataComponent>(gate).EntityPaused)
        {
            return null;
        }

        return entMan.GetComponent<GhostRoleComponent>(gate);
    }

    /// <summary>The ghost takes the role. Returns the Signal it became.</summary>
    private static EntityUid Join(TestPair pair, EntityUid scion)
    {
        var entMan = pair.Server.EntMan;
        var role = OfferedRole(entMan, scion);
        Assert.That(role, Is.Not.Null, "the stone offered no role.");
        Assert.That(entMan.System<GhostRoleSystem>().Takeover(pair.Player!, role!.Identifier), Is.True, "taking the role failed.");

        var body = pair.Player!.AttachedEntity;
        Assert.That(body, Is.Not.Null, "the player has no body after joining.");
        Assert.That(entMan.HasComponent<WFSignalComponent>(body), Is.True, "the player didn't become a Signal.");
        return body!.Value;
    }

    [Test]
    public void TheCapAndTheClimbOfPsiGrowWithTheStage()
    {
        Assert.That(WFSignalMath.Cap(WFMonolithStage.Quiet), Is.EqualTo(0), "a quiet stone takes Signals.");
        var cap = 0;
        var rate = 0f;
        for (var stage = WFMonolithStage.Hymn; stage <= WFMonolithStage.Crescendo; stage++)
        {
            Assert.That(WFSignalMath.Cap(stage), Is.GreaterThan(cap), $"the cap didn't grow at {stage}.");
            Assert.That(WFSignalMath.PsiRate(stage), Is.GreaterThan(rate), $"Psi didn't come back faster at {stage}.");
            cap = WFSignalMath.Cap(stage);
            rate = WFSignalMath.PsiRate(stage);
        }
    }

    [Test]
    public async Task AGhostCanJoinAtTheHymnButNotWhenQuiet()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid hymn = default, quiet = default, ghost = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            quiet = SpawnScion(entMan, At(map, -18, 18), 5f);
            ghost = MakeGhost(pair, map);
        });
        await pair.RunSeconds(2.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(OfferedRole(entMan, quiet), Is.Null, "a quiet stone offered a Signal role.");
            Assert.That(entMan.System<GhostRoleSystem>().GetGhostRoleCount(), Is.EqualTo(0), "a ghost role was listed with only a quiet stone about.");
            hymn = SpawnScion(entMan, At(map, 0, 0), 20f);
        });
        await pair.RunSeconds(2.5f);

        EntityUid signal = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.System<GhostRoleSystem>().GetGhostRoleCount(), Is.EqualTo(1), "the Hymn stone's role wasn't listed.");
            signal = Join(pair, hymn);
            var comp = entMan.GetComponent<WFSignalComponent>(signal);
            Assert.That(comp.Stone, Is.EqualTo(hymn), "the Signal answers to the wrong stone.");
            Assert.That(entMan.HasComponent<ActorComponent>(signal), Is.True, "the player isn't in the Signal.");
            Assert.That(entMan.HasComponent<GhostComponent>(signal), Is.False, "the Signal is a plain ghost.");
            Assert.That(entMan.HasComponent<Content.Shared.Mobs.Components.MobStateComponent>(signal), Is.False, "the Signal can be hurt.");
            Assert.That(OfferedRole(entMan, hymn), Is.Null, "a full stone still offered its role (cap 1 at the Hymn).");
        });

        // Dropping to Quiet lets the Signal go: its player is a ghost again.
        await server.WaitPost(() =>
        {
            entMan.GetComponent<WFMonolithComponent>(hymn).Resonance = 0f;
            entMan.System<WFMonolithSystem>().AddResonance((hymn, entMan.GetComponent<WFMonolithComponent>(hymn)), 0f);
        });
        await pair.RunSeconds(2.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(signal), Is.True, "the Signal outlived its stone going quiet.");
            Assert.That(entMan.HasComponent<GhostComponent>(pair.Player!.AttachedEntity), Is.True, "the player wasn't made a ghost when the stone went quiet.");
            Assert.That(OfferedRole(entMan, hymn), Is.Null, "a quiet stone offered a role.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASignalCannotLeaveItsStonesReach()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        EntityUid scion = default, signal = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 20f);
            MakeGhost(pair, map);
        });
        await pair.RunSeconds(2.5f);

        await server.WaitPost(() => signal = Join(pair, scion));

        // Across the deck is free; far off into space is not.
        await server.WaitPost(() => transforms.SetWorldPosition(signal, new Vector2(15f, 15f)));
        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSignalComponent>(signal);
            Assert.That(comp.Pulls, Is.EqualTo(0), "a Signal was pulled back from the middle of the ship.");
            Assert.That((transforms.GetWorldPosition(signal) - new Vector2(15f, 15f)).Length(), Is.LessThan(1f), "a Signal on the ship was moved.");
        });

        await server.WaitPost(() => transforms.SetWorldPosition(signal, new Vector2(400f, 400f)));
        await pair.RunSeconds(1f);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSignalComponent>(signal);
            var distance = (transforms.GetWorldPosition(signal) - transforms.GetWorldPosition(scion)).Length();
            Assert.That(comp.Pulls, Is.GreaterThan(0), "a Signal 400 m from its stone wasn't pulled back.");
            Assert.That(distance, Is.LessThan(40f), $"a Signal was pulled back to {distance:F1} m from its stone, outside the ship.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task APlayerDrivesARemadeWithItsThinkingPausedAndIsReturnedToTheSignal()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var mobState = entMan.System<MobStateSystem>();
        var signals = entMan.System<WFSignalSystem>();
        var minds = entMan.System<SharedMindSystem>();
        EntityUid scion = default, signal = default, remade = default, prey = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 20f, keepDirector: true);
            MakeGhost(pair, map);
            remade = entMan.SpawnEntity("WFRemadeSlasher", At(map, 3, 0));
            entMan.System<WFRemadeDirectorSystem>().Enlist(remade, scion);
            prey = entMan.SpawnEntity("MobCarp", At(map, -15, 15));
        });
        await pair.RunSeconds(2.5f);

        await server.WaitPost(() => signal = Join(pair, scion));

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSignalComponent>(signal);
            comp.Psi = WFSignalMath.MaxPsi;
            Assert.That(entMan.HasComponent<WFRemadeBehaviourComponent>(remade), Is.True, "the Director doesn't hold the Remade to begin with.");
            Assert.That(signals.TryPossess((signal, comp), remade), Is.True, "the Signal couldn't take a Remade 3 m off.");

            Assert.That(pair.Player!.AttachedEntity, Is.EqualTo(remade), "the player doesn't control the Remade.");
            Assert.That(entMan.GetComponent<HTNComponent>(remade).Enabled, Is.False, "the Remade's planning still runs.");
            Assert.That(entMan.HasComponent<ActiveNPCComponent>(remade), Is.False, "the Remade is still an awake NPC.");
            Assert.That(entMan.HasComponent<WFRemadeBehaviourComponent>(remade), Is.False, "the Director still steers the driven Remade.");
            Assert.That(comp.Possessing, Is.EqualTo(remade));
            Assert.That(comp.Psi, Is.EqualTo(WFSignalMath.MaxPsi - WFSignalMath.PossessCost).Within(0.01f), "taking over cost the wrong Psi.");

            // The abilities are actions, and the shout works when asked.
            var possessed = entMan.GetComponent<WFSignalPossessedComponent>(remade);
            var abilities = entMan.GetComponent<WFRemadeAbilitiesComponent>(remade);
            var names = possessed.Actions.Select(action => entMan.GetComponent<MetaDataComponent>(action).EntityPrototype?.ID).ToList();
            Assert.That(names, Does.Contain("WFSignalActionRelease"), "no way to let go.");
            Assert.That(names, Does.Contain("WFSignalActionShout"), "the slasher's shout isn't an action.");
            Assert.That(names, Does.Contain("WFSignalActionCharge"), "the slasher's charge isn't an action.");

            var shout = new WFSignalAbilityEvent { Ability = "WFShoutAbility", Performer = remade, Target = At(map, 3, 0) };
            entMan.EventBus.RaiseLocalEvent(remade, (object) shout, true);
            Assert.That(shout.Handled, Is.True, "the driven Remade's shout didn't go off.");
            Assert.That(entMan.System<WFAbilitySystem>().Find<WFShoutAbility>(remade)!.NextUse, Is.GreaterThan(TimeSpan.Zero), "the shout didn't start its cooldown.");
            Assert.That(abilities.Abilities, Is.Not.Empty);
        });

        // Let go: the player is a Signal again and the Remade is the Director's.
        await server.WaitPost(() =>
        {
            var release = new WFSignalReleaseEvent { Performer = remade };
            entMan.EventBus.RaiseLocalEvent(remade, (object) release, true);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.AttachedEntity, Is.EqualTo(signal), "letting go didn't return the player to the Signal.");
            Assert.That(entMan.GetComponent<HTNComponent>(remade).Enabled, Is.True, "the Remade's planning stayed off after release.");
            Assert.That(entMan.HasComponent<WFRemadeBehaviourComponent>(remade), Is.True, "the Director didn't take the Remade back.");
            Assert.That(entMan.HasComponent<WFSignalPossessedComponent>(remade), Is.False);
            Assert.That(entMan.GetComponent<WFSignalComponent>(signal).Possessing, Is.Null);
            Assert.That(minds.TryGetMind(signal, out _, out var mind) && mind.VisitingEntity == null, Is.True, "the mind is still visiting.");
        });

        // Take it again, then lose it: the Remade dying returns the player to the Signal.
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<WFSignalComponent>(signal);
            comp.PossessReady = TimeSpan.Zero;
            Assert.That(signals.TryPossess((signal, comp), remade), Is.True, "the Signal couldn't take the Remade a second time.");
            Assert.That(pair.Player!.AttachedEntity, Is.EqualTo(remade));
            mobState.ChangeMobState(remade, MobState.Dead);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.AttachedEntity, Is.EqualTo(signal), "the Remade dying didn't return the player to the Signal.");
            Assert.That(entMan.HasComponent<WFSignalPossessedComponent>(remade), Is.False, "a dead Remade is still marked as driven.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ASignalCannotTakeTheUndyingOrAnotherStonesRemade()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var signals = entMan.System<WFSignalSystem>();
        EntityUid scion = default, signal = default, undying = default, foreign = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 20f, keepDirector: true);
            MakeGhost(pair, map);
            undying = entMan.SpawnEntity("WFRemadeUndying", At(map, 3, 0));
            entMan.System<WFRemadeDirectorSystem>().Enlist(undying, scion);
            foreign = entMan.SpawnEntity("WFRemadeSlasher", At(map, 0, 3));
            entMan.EnsureComponent<WFRemadeComponent>(foreign);
        });
        await pair.RunSeconds(2.5f);

        await server.WaitPost(() => signal = Join(pair, scion));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSignalComponent>(signal);
            comp.Psi = WFSignalMath.MaxPsi;
            Assert.That(signals.TryPossess((signal, comp), undying), Is.False, "a Signal took the Undying.");
            Assert.That(signals.TryPossess((signal, comp), foreign), Is.False, "a Signal took a Remade that isn't its stone's.");
            Assert.That(comp.Psi, Is.EqualTo(WFSignalMath.MaxPsi), "a refused takeover cost Psi.");
            Assert.That(pair.Player!.AttachedEntity, Is.EqualTo(signal));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ATrickSpendsPsiAndWaitsOutItsCooldown()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var signals = entMan.System<WFSignalSystem>();
        EntityUid scion = default, signal = default, door = default, bait = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 20f);
            MakeGhost(pair, map);
            bait = entMan.SpawnEntity("MobCarp", At(map, 4, 0));
            door = entMan.SpawnEntity("Airlock", At(map, 5, 3));
            entMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
        });
        await pair.RunSeconds(2.5f);

        await server.WaitPost(() => signal = Join(pair, scion));
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSignalComponent>(signal);
            var flicker = WFSignalMath.Info(WFSignalTrick.Flicker);

            comp.Psi = 50f;
            Assert.That(signals.TryTrick((signal, comp), WFSignalTrick.Flicker, bait), Is.True, "the first flicker failed.");
            Assert.That(comp.Psi, Is.EqualTo(50f - flicker.Cost).Within(0.01f), "the flicker cost the wrong Psi.");

            Assert.That(signals.TryTrick((signal, comp), WFSignalTrick.Flicker, bait), Is.False, "a flicker ran inside its cooldown.");
            Assert.That(comp.Psi, Is.EqualTo(50f - flicker.Cost).Within(0.01f), "a refused flicker cost Psi.");
            Assert.That(comp.Tricks, Is.EqualTo(1));

            // The Hymn doesn't reach doors yet.
            Assert.That(signals.TryTrick((signal, comp), WFSignalTrick.Door, door), Is.False, "a door trick worked at the Hymn.");

            comp.Psi = 0.5f;
            comp.Ready.Clear();
            Assert.That(signals.TryTrick((signal, comp), WFSignalTrick.Flicker, bait), Is.False, "a flicker worked with no Psi.");

            // At Growth the door trick opens.
            entMan.GetComponent<WFMonolithComponent>(scion).Resonance = 40f;
            entMan.System<WFMonolithSystem>().AddResonance((scion, entMan.GetComponent<WFMonolithComponent>(scion)), 0f);
            comp.Psi = 50f;
            Assert.That(signals.TryTrick((signal, comp), WFSignalTrick.Door, door), Is.True, "the door trick failed at Growth.");
            Assert.That(entMan.GetComponent<DoorComponent>(door).State, Is.Not.EqualTo(DoorState.Closed), "the door didn't move.");
        });

        // The cooldown ends.
        await pair.RunSeconds(WFSignalMath.Info(WFSignalTrick.Flicker).Cooldown + 1f);
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WFSignalComponent>(signal);
            comp.Psi = 50f;
            Assert.That(signals.TryTrick((signal, comp), WFSignalTrick.Flicker, bait), Is.True, "the flicker didn't come back after its cooldown.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DestroyingTheStoneLetsItsSignalsGoToGhosts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid scion = default, signal = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            scion = SpawnScion(entMan, At(map, 0, 0), 20f);
            MakeGhost(pair, map);
        });
        await pair.RunSeconds(2.5f);

        await server.WaitPost(() => signal = Join(pair, scion));
        await server.WaitPost(() => entMan.DeleteEntity(scion));
        await pair.RunSeconds(1.5f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(signal), Is.True, "the Signal outlived its stone.");
            Assert.That(entMan.HasComponent<GhostComponent>(pair.Player!.AttachedEntity), Is.True, "the player wasn't made a ghost when the stone was destroyed.");
            Assert.That(entMan.EntityQueryEnumerator<WFSignalGateComponent>().MoveNext(out _, out _), Is.False, "the gate outlived its stone.");
        });

        await pair.CleanReturnAsync();
    }
}
