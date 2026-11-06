#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Remade.Betrayal;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Chat.Systems;
using Content.Server.Medical.SuitSensors;
using Content.Server.Radio;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Betrayal;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>Remembers what was said aloud and what went out over the radio.</summary>
public sealed class WFBetrayalObserver : EntitySystem
{
    public readonly List<(EntityUid Source, string Message)> Spoke = new();
    public readonly List<(string Channel, EntityUid Source)> Radio = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntitySpokeEvent>(ev => Spoke.Add((ev.Source, ev.Message)));
        SubscribeLocalEvent<RadioSendAttemptEvent>(OnSend);
    }

    private void OnSend(ref RadioSendAttemptEvent ev)
    {
        Radio.Add((ev.Channel.ID, ev.RadioSource));
    }
}

/// <summary>The dead's own voices, the crew monitor's ghosts, bodies that move when unseen and a mind that breaks and lies.</summary>
[TestFixture]
public sealed class BetrayalTest
{
    private static EntityCoordinates At(TestMapData map, float x, float y) => WFRemadeDirectorTest.At(map, x, y);

    private static EntityUid SpawnDeadHuman(IEntityManager entMan, EntityCoordinates at)
    {
        var human = entMan.SpawnEntity("MobHuman", at);
        entMan.System<MobStateSystem>().ChangeMobState(human, MobState.Dead);
        return human;
    }

    /// <summary>A Scion with its Director removed, so a test sees only what the betrayal systems do.</summary>
    private static EntityUid SpawnScion(IEntityManager entMan, EntityCoordinates at, float resonance)
    {
        var uid = entMan.SpawnEntity("WFMonolithScion", at);
        entMan.GetComponent<WFRemadeDirectorComponent>(uid).BuildUp = false;
        entMan.RemoveComponent<WFRemadeDirectorComponent>(uid);
        var comp = entMan.GetComponent<WFMonolithComponent>(uid);
        comp.Resonance = resonance;
        entMan.System<WFMonolithSystem>().AddResonance((uid, comp), 0.01f);
        return uid;
    }

    [Test]
    public void TheMathScalesAndGarbles()
    {
        Assert.That(WFBetrayalMath.ScoreLayers(10f), Is.EqualTo(0), "the score played under Growth.");
        Assert.That(WFBetrayalMath.ScoreLayers(55f), Is.EqualTo(2));
        Assert.That(WFBetrayalMath.ScoreLayers(100f), Is.EqualTo(WFBetrayalMath.MaxLayers), "the Crescendo didn't play every layer.");
        Assert.That(WFBetrayalMath.Breaking(30f), Is.EqualTo(0f));
        Assert.That(WFBetrayalMath.Breaking(100f), Is.EqualTo(1f));

        var sequence = new Queue<float>(Enumerable.Repeat(0.01f, 100));
        var garbled = WFBetrayalMath.Corrupt("help me please", 0.5f, () => sequence.Count > 0 ? sequence.Dequeue() : 0.9f);
        Assert.That(garbled, Is.Not.EqualTo("help me please"), "corruption changed nothing.");
        Assert.That(WFBetrayalMath.Corrupt("help me please", 0f, () => 0f), Is.EqualTo("help me please"), "corruption at 0 touched the text.");
    }

    [Test]
    public async Task ATurnedCrewmansRemadeCallsOutOneOfTheirLastLines()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var voice = entMan.System<WFBetrayalVoiceSystem>();
        var observer = entMan.System<WFBetrayalObserver>();
        var lines = new List<string>();
        EntityUid human = default;

        await server.WaitPost(() =>
        {
            observer.Spoke.Clear();
            // A Remade that is hunting is in combat and keeps quiet, so this one is left idle.
            server.CfgMan.SetCVar(CCVars.NPCEnabled, false);
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 8, 0));

            // No Monolith stirring: an ordinary death stays quiet.
            var quiet = entMan.SpawnEntity("MobHuman", At(map, 2, 0));
            var quietSet = entMan.SpawnEntity("ClothingHeadsetGrey", At(map, 2, 0));
            Assert.That(entMan.System<InventorySystem>().TryEquip(quiet, quietSet, "ears", force: true), Is.True);
            entMan.System<MobStateSystem>().ChangeMobState(quiet, MobState.Dead);
            Assert.That(entMan.HasComponent<WFLureComponent>(quietSet), Is.False, "a headset pleaded with no Monolith about.");

            SpawnScion(entMan, At(map, -6, 0), 20f);
            human = entMan.SpawnEntity("MobHuman", At(map, 0, 0));
            for (var i = 1; i <= 7; i++)
            {
                voice.Remember(human, $"Is anyone in medbay? {i}");
            }

            voice.Remember(human, "ok");
            lines = entMan.GetComponent<WFLastWordsComponent>(human).Lines.ToList();
            Assert.That(lines, Has.Count.EqualTo(WFBetrayalVoiceSystem.KeptLines), "the kept lines weren't capped.");
            Assert.That(lines, Does.Not.Contain("ok"), "a line too short to call out was kept.");
            Assert.That(lines.Last(), Is.EqualTo("Is anyone in medbay? 7"), "the newest line wasn't kept.");

            entMan.System<MobStateSystem>().ChangeMobState(human, MobState.Dead);
            Assert.That(entMan.System<WFRemadeConversionSystem>().TryStartConversion(human, null, null, instant: true), Is.True);
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(human), 10);
        EntityUid remade = default;
        await server.WaitAssertion(() =>
        {
            var callers = entMan.AllEntities<WFCallerComponent>().ToList();
            Assert.That(callers, Has.Count.EqualTo(1), "the Remade kept nothing of what the crewman said.");
            remade = callers[0];
            Assert.That(entMan.HasComponent<WFRemadeComponent>(remade), Is.True);
            Assert.That(callers[0].Comp.Name, Is.Not.Empty);
            callers[0].Comp.NextCall = TimeSpan.Zero;
        });

        await pair.RunSeconds(7f);

        await server.WaitAssertion(() =>
        {
            var caller = entMan.GetComponent<WFCallerComponent>(remade);
            Assert.That(caller.Calls, Is.GreaterThan(0), $"the Remade never called out with crew 8 m away: in combat {voice.InCombat(remade)}, crew near {voice.CrewNear(remade, entMan.GetComponent<TransformComponent>(remade), WFBetrayalVoiceSystem.CallRange)}, dead {entMan.System<MobStateSystem>().IsDead(remade)}, playing dead {entMan.HasComponent<WFRemadePlayingDeadComponent>(remade)}, next {caller.NextCall}; actors {string.Join(", ", entMan.AllEntities<Robust.Shared.Player.ActorComponent>().Select(a => entMan.ToPrettyString(a.Owner) + " at " + entMan.System<SharedTransformSystem>().GetWorldPosition(a.Owner)))}; remade at {entMan.System<SharedTransformSystem>().GetWorldPosition(remade)}.");
            Assert.That(lines, Does.Contain(caller.LastLine), "it called a line the crewman never said.");
            Assert.That(observer.Spoke.Any(said => said.Source == remade && said.Message == caller.LastLine), Is.True,
                "the Remade never spoke the line aloud.");
        });
        await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.NPCEnabled, true));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ADeadCrewmansHeadsetPleadsOnTheirChannelAFewTimesAndStops()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var voice = entMan.System<WFBetrayalVoiceSystem>();
        var observer = entMan.System<WFBetrayalObserver>();
        EntityUid human = default, headset = default;
        var promised = 0;

        await server.WaitPost(() =>
        {
            observer.Radio.Clear();
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 8, 0));
            human = entMan.SpawnEntity("MobHuman", At(map, 0, 0));
            voice.Remember(human, "Is anyone in medbay?");
            headset = entMan.SpawnEntity("ClothingHeadsetGrey", At(map, 0, 0));
            Assert.That(entMan.System<InventorySystem>().TryEquip(human, headset, "ears", force: true), Is.True);
            entMan.System<MobStateSystem>().ChangeMobState(human, MobState.Dead);

            var lure = entMan.GetComponent<WFLureComponent>(headset);
            promised = lure.Remaining;
            Assert.That(promised, Is.InRange(WFBetrayalVoiceSystem.LureMin, WFBetrayalVoiceSystem.LureMax));
            Assert.That(lure.Name, Is.EqualTo(entMan.GetComponent<MetaDataComponent>(human).EntityName), "the plea isn't in the dead crewman's name.");
            Assert.That(lure.Area, Is.Not.Empty, "the plea has no place in it.");
        });

        for (var round = 0; round < promised + 2; round++)
        {
            await server.WaitPost(() =>
            {
                if (entMan.TryGetComponent<WFLureComponent>(headset, out var lure))
                    lure.Next = TimeSpan.Zero;
            });
            await pair.RunSeconds(6f);
        }

        await server.WaitAssertion(() =>
        {
            var channels = entMan.GetComponent<EncryptionKeyHolderComponent>(headset).Channels;
            Assert.That(channels, Is.Not.Empty);
            Assert.That(observer.Radio, Has.Count.EqualTo(promised), "the headset didn't send exactly the pleas it was due.");
            Assert.That(observer.Radio.All(sent => sent.Source == headset && channels.Contains(sent.Channel)), Is.True,
                "a plea went out on a channel the headset doesn't have, or from something else.");
            Assert.That(entMan.HasComponent<WFLureComponent>(headset), Is.False, "the headset kept pleading after its last.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ATurnedCrewmanStillReadsAliveOnTheSuitSensors()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid human = default, suit = default;

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 8, 0));
            human = entMan.SpawnEntity("MobHuman", At(map, 0, 0));
            suit = entMan.SpawnEntity("ClothingUniformJumpsuitColorGrey", At(map, 0, 0));
            var card = entMan.SpawnEntity("PassengerIDCard", At(map, 0, 0));
            var inventory = entMan.System<InventorySystem>();
            Assert.That(inventory.TryEquip(human, suit, "jumpsuit", force: true), Is.True);
            Assert.That(inventory.TryEquip(human, card, "id", force: true), Is.True);
            Assert.That(entMan.System<SharedIdCardSystem>().TryChangeFullName(card, "Rook Vance", entMan.GetComponent<IdCardComponent>(card)), Is.True);
            var sensors = entMan.System<SuitSensorSystem>();
            sensors.SetSensor((suit, entMan.GetComponent<SuitSensorComponent>(suit)), SuitSensorMode.SensorCords, human);

            Assert.That(sensors.GetSensorState(suit)?.IsAlive, Is.True, "the living crewman didn't read alive to begin with.");
            entMan.System<MobStateSystem>().ChangeMobState(human, MobState.Dead);
            Assert.That(sensors.GetSensorState(suit)?.IsAlive, Is.False, "the dead crewman didn't read dead.");
            Assert.That(entMan.System<WFRemadeConversionSystem>().TryStartConversion(human, null, null, instant: true), Is.True);
        });

        await WFRemadeDirectorTest.RunUntil(pair, () => entMan.Deleted(human), 10);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(suit), Is.False, "the suit didn't survive the turning.");
            Assert.That(entMan.System<SuitSensorSystem>().GetSensorState(suit), Is.Null, "the dropped suit still reads on its own; the ghost proves nothing.");

            var ghosts = entMan.AllEntities<WFSensorGhostComponent>().ToList();
            Assert.That(ghosts, Has.Count.EqualTo(1), "the Remade kept no sensor reading.");
            var reading = entMan.System<WFSensorGhostSystem>().Reading(ghosts[0]);
            Assert.That(reading.IsAlive, Is.True, "the turned crewman reads dead on the crew monitor.");
            Assert.That(reading.Name, Is.EqualTo("Rook Vance"), "the reading lost the crewman's name.");
            Assert.That(reading.Coordinates, Is.Not.Null, "the reading has no position.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ACorpseNeverMovesInSightAndMovesWhenNoOneCanSee()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var transforms = entMan.System<SharedTransformSystem>();
        var corpses = entMan.System<WFRestlessCorpseSystem>();
        EntityUid corpse = default;
        var before = (Vector2.Zero, Angle.Zero);

        await server.WaitPost(() =>
        {
            WFRemadeDirectorTest.LayDeck(pair, map);
            SpawnScion(entMan, At(map, -10, 10), 40f);
            corpse = SpawnDeadHuman(entMan, At(map, 0, 0));
            WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 8, 0));
            before = (transforms.GetWorldPosition(corpse), transforms.GetWorldRotation(corpse));
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < 25; i++)
            {
                corpses.Pass(force: true);
            }

            Assert.That(corpses.Moved, Is.EqualTo(0), "a corpse moved with a player looking straight at it.");
            Assert.That(transforms.GetWorldPosition(corpse), Is.EqualTo(before.Item1));
            Assert.That(transforms.GetWorldRotation(corpse), Is.EqualTo(before.Item2));
        });

        // A wall between them: nobody has a line to the body now.
        await server.WaitPost(() => entMan.SpawnEntity("WallSolid", At(map, 4, 0)));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                corpses.Pass(force: true);
            }

            var after = (transforms.GetWorldPosition(corpse), transforms.GetWorldRotation(corpse));
            Assert.That(corpses.Moved, Is.GreaterThan(0), "a corpse out of every player's sight never moved.");
            Assert.That(after, Is.Not.EqualTo(before), "the corpse is where it was, though it was moved.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AStressedPlayerIsToldAFalseAnnouncementOnTheirOwnSessionOnly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid player = default, bystander = default;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WFBetrayalCVars.Rate, 60f);
            WFRemadeDirectorTest.LayDeck(pair, map);
            SpawnScion(entMan, At(map, -10, 10), 100f);
            player = WFRemadeDirectorTest.SpawnCrewman(pair, At(map, 4, 0));
            bystander = entMan.SpawnEntity("MobCarp", At(map, 6, 0));
            entMan.EnsureComponent<WFRemadeStressComponent>(player).Stress = 100f;
            entMan.EnsureComponent<WFRemadeStressComponent>(bystander).Stress = 100f;
        });

        await pair.RunSeconds(8f);

        await server.WaitAssertion(() =>
        {
            var subject = entMan.GetComponent<WFBetrayalSubjectComponent>(player);
            Assert.That(subject.Announcements, Is.GreaterThan(0), "a breaking player under a Crescendo was never told a false announcement.");
            Assert.That(subject.LastAnnouncement, Is.Not.Null.And.Not.Empty);
            Assert.That(entMan.GetComponent<WFRemadeStressComponent>(player).Stress, Is.GreaterThan(0f));

            // The lies go to a session. A body nobody plays has none, so it was told nothing.
            Assert.That(entMan.TryGetComponent<WFBetrayalSubjectComponent>(bystander, out var other) ? other.Announcements + other.Messages : 0,
                Is.EqualTo(0), "an unplayed body was sent a lie.");
            Assert.That(entMan.HasComponent<WFShakenAimComponent>(player), Is.True, "a player at breaking point had a steady hand.");
        });

        await server.WaitPost(() => server.CfgMan.SetCVar(WFBetrayalCVars.Rate, 1f));
        await pair.CleanReturnAsync();
    }
}
