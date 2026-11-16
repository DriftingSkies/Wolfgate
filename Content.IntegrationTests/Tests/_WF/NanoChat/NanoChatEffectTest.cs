#nullable enable
using System.Linq;
using Content.Server._WF.NanoChat;
using Content.Server.CartridgeLoader;
using Content.Shared._WF.NanoChat;
using Content.Shared.CartridgeLoader;
using Content.Shared.Drunk;
using Content.Shared.PDA;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NanoChat;

/// <summary>A message keeps the effect it was sent with, and a drunk sender's message arrives with typos.</summary>
[TestFixture]
[TestOf(typeof(NanoChatTypingSystem))]
public sealed class NanoChatEffectTest
{
    private const string Pda = "PassengerPDA";
    private const string Server = "TelecomServerFilledCommon";

    private const string Text =
        "the quick brown fox jumps over the lazy dog while the station drifts quietly past another rock field";

    [Test]
    public async Task EffectsAndDrunkTyping()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid a = default, b = default, typist = default;
        await server.WaitPost(() =>
        {
            var telecom = entMan.SpawnEntity(Server, map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(telecom, false);
            a = entMan.SpawnEntity(Pda, map.GridCoords);
            b = entMan.SpawnEntity(Pda, map.GridCoords);
            typist = entMan.SpawnEntity("MobHuman", map.GridCoords);
        });

        // The power net updates twice a second.
        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var typing = entMan.System<NanoChatTypingSystem>();
            Assert.That(typing.TypoRate(typist), Is.Zero);
            Assert.That(typing.Impair(typist, Text), Is.EqualTo(Text), "A sober typist made typos.");

            var to = CardOf(entMan, b).Comp.Number!.Value;
            Send(entMan, a, a, to, "wave hello", NanoChatEffect.Wave);
            Assert.That(CardOf(entMan, b).Comp.Messages[CardOf(entMan, a).Comp.Number!.Value].Single().Effect,
                Is.EqualTo(NanoChatEffect.Wave));

            entMan.System<SharedDrunkSystem>().TryApplyDrunkenness(typist, 1500, false);
            Assert.That(typing.TypoRate(typist), Is.GreaterThan(0.1f));
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            var from = CardOf(entMan, a).Comp.Number!.Value;
            var to = CardOf(entMan, b).Comp.Number!.Value;

            // An unknown effect is dropped; the drunk typist's text arrives changed.
            Send(entMan, a, typist, to, Text, (NanoChatEffect) 200);
            var received = CardOf(entMan, b).Comp.Messages[from].Last();
            Assert.That(received.Effect, Is.EqualTo(NanoChatEffect.None));
            Assert.That(received.Content, Is.Not.EqualTo(Text), "A drunk typist's message arrived without typos.");
            Assert.That(received.Content.Length, Is.LessThanOrEqualTo(NanoChatMessage.MaxContentLength));
        });

        await pair.CleanReturnAsync();
    }

    private static Entity<NanoChatCardComponent> CardOf(IEntityManager entMan, EntityUid pda)
    {
        var id = entMan.GetComponent<PdaComponent>(pda).ContainedId!.Value;
        return (id, entMan.GetComponent<NanoChatCardComponent>(id));
    }

    private static void Send(IEntityManager entMan, EntityUid pda, EntityUid actor, uint to, string text, NanoChatEffect effect)
    {
        Assert.That(entMan.System<CartridgeLoaderSystem>().TryGetProgram<NanoChatCartridgeComponent>(pda, out var app, out _));
        var msg = new NanoChatUiMessageEvent(NanoChatUiMessageType.SendMessage, to, text, effect: effect)
        {
            LoaderUid = entMan.GetNetEntity(pda),
            Actor = actor,
            User = actor,
        };
        entMan.EventBus.RaiseLocalEvent<CartridgeMessageEvent>(app!.Value, msg);
    }
}
