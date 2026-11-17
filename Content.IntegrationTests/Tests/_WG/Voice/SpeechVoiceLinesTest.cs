#nullable enable
using System.Numerics;
using Content.Client._WG.Voice;
using Content.IntegrationTests.Pair;
using Content.Server.Chat.Systems;
using Content.Server.Radio;
using Content.Shared._EinsteinEngines.Language;
using Content.Shared._WG.RMCCore.Deafness;
using Content.Shared._WG.Voice;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Radio;
using Content.Shared.Speech;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Voice;

/// <summary>Who is sent a voice line, and how many lines one client plays at once.</summary>
[TestFixture]
public sealed class SpeechVoiceLinesTest
{
    private static readonly ProtoId<SpeechSoundsPrototype> PickedSound = "WGMoth";

    private const string LongLine = "this line runs long enough to keep playing for the whole of the test";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: WGTestVoiceSpeakerOff
          components:
          - type: ActiveRadio
          - type: RadioSpeaker
            enabled: true
            powerRequired: true
          - type: ApcPowerReceiver

        - type: entity
          id: WGTestVoiceSpeakerOn
          components:
          - type: ActiveRadio
          - type: RadioSpeaker
            enabled: true
        """;

    [Test]
    public async Task LinesReachOnlyThoseWhoCanHear()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var chat = entMan.System<ChatSystem>();
        var voices = pair.Client.EntMan.System<SpeechVoiceSystem>();
        var map = await pair.CreateTestMap();
        var talker = EntityUid.Invalid;
        var off = EntityUid.Invalid;
        var on = EntityUid.Invalid;

        var listener = await SpawnListener(pair, map.GridCoords);
        await server.WaitPost(() =>
        {
            talker = SpawnTalker(pair, map.GridCoords.Offset(new Vector2(1f, 0f)));
            off = entMan.SpawnEntity("WGTestVoiceSpeakerOff", map.GridCoords);
            on = entMan.SpawnEntity("WGTestVoiceSpeakerOn", map.GridCoords);
        });
        await pair.RunTicksSync(5);
        var start = voices.ActiveLines;

        await server.WaitPost(() => chat.TrySendInGameICMessage(talker, LongLine, InGameICChatType.Speak, false));
        await pair.RunTicksSync(5);
        Assert.That(voices.ActiveLines - start, Is.EqualTo(1), "a hearing listener gets the line");

        await server.WaitPost(() =>
        {
            Assert.That(entMan.System<SharedPowerReceiverSystem>().IsPowered(off), Is.False);
            Receive(pair, off, talker, off, "unpowered speaker" + LongLine);
            Receive(pair, on, talker, on, "its own radio" + LongLine);
        });
        await pair.RunTicksSync(5);
        Assert.That(voices.ActiveLines - start, Is.EqualTo(1), "an unpowered or sending loudspeaker stays quiet");

        await server.WaitPost(() => Receive(pair, on, talker, talker, "working speaker" + LongLine));
        await pair.RunTicksSync(5);
        Assert.That(voices.ActiveLines - start, Is.EqualTo(2), "a working loudspeaker plays to a listener beside it");

        await server.WaitPost(() =>
        {
            entMan.EnsureComponent<DeafComponent>(listener);
            chat.TrySendInGameICMessage(talker, "deaf speech" + LongLine, InGameICChatType.Speak, false);
            Receive(pair, on, talker, talker, "deaf speaker" + LongLine);
        });
        await pair.RunTicksSync(5);
        Assert.That(voices.ActiveLines - start, Is.EqualTo(2), "a deaf listener gets no speech and no loudspeaker");

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClientLimitsLinesPerSourceAndRadio()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var voices = client.EntMan.System<SpeechVoiceSystem>();
        var map = await pair.CreateTestMap();
        var talkerA = NetEntity.Invalid;
        var talkerB = NetEntity.Invalid;

        await SpawnListener(pair, map.GridCoords);
        await pair.Server.WaitPost(() =>
        {
            talkerA = pair.Server.EntMan.GetNetEntity(SpawnTalker(pair, map.GridCoords));
            talkerB = pair.Server.EntMan.GetNetEntity(SpawnTalker(pair, map.GridCoords));
        });
        await pair.RunTicksSync(5);

        void Send(EntityEventArgs ev)
        {
            client.EntMan.EventBus.RaiseEvent(EventSource.Network, (object) ev);
        }

        await client.WaitPost(() =>
        {
            var start = voices.ActiveLines;

            for (var i = 0; i < 6; i++)
            {
                Send(new SpeechVoiceRadioEvent(NetEntity.Invalid, LongLine, new SpeechVoice()));
            }

            Assert.That(voices.ActiveLines - start, Is.EqualTo(4), "radio takes at most four lines");

            for (var i = 0; i < 3; i++)
            {
                Send(new SpeechVoiceSpokeEvent(talkerA, LongLine, false, null));
            }

            Assert.That(voices.ActiveLines - start, Is.EqualTo(6), "one talker takes at most two lines");

            for (var i = 0; i < 3; i++)
            {
                Send(new SpeechVoiceSpokeEvent(talkerB, LongLine, false, null));
            }

            Assert.That(voices.ActiveLines - start, Is.EqualTo(8), "a busy radio leaves room for people nearby");

            voices.Preview(LongLine, new SpeechVoice());
            Assert.That(voices.ActiveLines - start, Is.EqualTo(9), "the Test button plays past the cap");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VolumeAtZeroPlaysNothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var voices = client.EntMan.System<SpeechVoiceSystem>();

        await client.WaitPost(() =>
        {
            var start = voices.ActiveLines;

            client.CfgMan.SetCVar(SpeechVoiceCVars.Volume, 0f);
            voices.Preview(LongLine, new SpeechVoice());
            Assert.That(voices.ActiveLines, Is.EqualTo(start), "a muted voice isn't rendered");

            client.CfgMan.SetCVar(SpeechVoiceCVars.Volume, SpeechVoiceCVars.Volume.DefaultValue);
            voices.Preview(LongLine, new SpeechVoice());
            Assert.That(voices.ActiveLines, Is.EqualTo(start + 1));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A character on the message sound sends no voice, spoken or by radio.</summary>
    [Test]
    public async Task CharacterOnMessageSoundSendsNoVoice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var chat = entMan.System<ChatSystem>();
        var voices = pair.Client.EntMan.System<SpeechVoiceSystem>();
        var map = await pair.CreateTestMap();
        var talker = EntityUid.Invalid;
        var on = EntityUid.Invalid;

        await SpawnListener(pair, map.GridCoords);
        await server.WaitPost(() =>
        {
            talker = SpawnTalker(pair, map.GridCoords.Offset(new Vector2(1f, 0f)));
            on = entMan.SpawnEntity("WGTestVoiceSpeakerOn", map.GridCoords);
            entMan.GetComponent<SpeechVoiceComponent>(talker).Voice.Enabled = false;
        });
        await pair.RunTicksSync(5);
        var start = voices.ActiveLines;

        await server.WaitPost(() =>
        {
            chat.TrySendInGameICMessage(talker, LongLine, InGameICChatType.Speak, false);
            Receive(pair, on, talker, talker, "by radio" + LongLine);
        });
        await pair.RunTicksSync(5);
        Assert.That(voices.ActiveLines, Is.EqualTo(start));

        await pair.CleanReturnAsync();
    }

    /// <summary>A listener with voices off hears the message sound the speaker picked, which only the server knows.</summary>
    [Test]
    public async Task ListenerWithVoicesOffHearsPickedMessageSound()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var server = pair.Server;
        var entMan = server.EntMan;
        var voices = client.EntMan.System<SpeechVoiceSystem>();
        var map = await pair.CreateTestMap();
        var talker = EntityUid.Invalid;

        await SpawnListener(pair, map.GridCoords);
        await server.WaitPost(() =>
        {
            talker = SpawnTalker(pair, map.GridCoords.Offset(new Vector2(1f, 0f)));
            entMan.GetComponent<SpeechComponent>(talker).SpeechSounds = PickedSound;
        });
        await pair.RunTicksSync(5);
        await client.WaitPost(() => client.CfgMan.SetCVar(SpeechVoiceCVars.Enabled, false));
        var start = voices.ActiveLines;

        await server.WaitPost(() => entMan.System<ChatSystem>().TrySendInGameICMessage(talker, LongLine, InGameICChatType.Speak, false));
        await pair.RunTicksSync(5);
        Assert.That(voices.ActiveLines, Is.EqualTo(start), "no voice plays");
        Assert.That(voices.LastMessageSound, Is.EqualTo(PickedSound), "the speaker's own pick plays, not the species' default");

        await client.WaitPost(() => client.CfgMan.SetCVar(SpeechVoiceCVars.Enabled, SpeechVoiceCVars.Enabled.DefaultValue));
        await pair.CleanReturnAsync();
    }

    /// <summary>The choice survives the database, and rows saved before it existed keep the voice.</summary>
    [Test]
    public void ChoiceSurvivesStorage()
    {
        var stored = Content.Server._WG.Voice.SpeechVoiceJson.ToStored(new SpeechVoice { Enabled = false });
        Assert.That(Content.Server._WG.Voice.SpeechVoiceJson.FromStored(stored).Enabled, Is.False);
        Assert.That(Content.Server._WG.Voice.SpeechVoiceJson.FromStored(stored.Replace("\"Enabled\":false,", string.Empty)).Enabled, Is.True);
    }

    private static async Task<EntityUid> SpawnListener(TestPair pair, EntityCoordinates coords)
    {
        var entMan = pair.Server.EntMan;
        var minds = entMan.System<SharedMindSystem>();
        var session = pair.Server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var listener = EntityUid.Invalid;

        await pair.Server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            listener = entMan.SpawnEntity("MobHuman", coords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, listener);
        });

        return listener;
    }

    /// <summary>A talker on the slowest voice, so their lines outlast the test.</summary>
    private static EntityUid SpawnTalker(TestPair pair, EntityCoordinates coords)
    {
        var entMan = pair.Server.EntMan;
        var talker = entMan.SpawnEntity("MobHuman", coords);
        var comp = entMan.EnsureComponent<SpeechVoiceComponent>(talker);
        comp.Voice = new SpeechVoice { Speed = SpeechVoice.MaxSpeed };
        entMan.Dirty(talker, comp);
        return talker;
    }

    private static void Receive(TestPair pair, EntityUid receiver, EntityUid talker, EntityUid radioSource, string text)
    {
        var protos = pair.Server.ProtoMan;
        var message = new ChatMessage(ChatChannel.Radio, text, text, NetEntity.Invalid, null);
        var ev = new RadioReceiveEvent(talker,
            protos.Index<RadioChannelPrototype>(SharedChatSystem.CommonChannel),
            message,
            message,
            protos.Index<LanguagePrototype>("TauCetiBasic"),
            radioSource);
        pair.Server.EntMan.EventBus.RaiseLocalEvent(receiver, ref ev);
    }
}
