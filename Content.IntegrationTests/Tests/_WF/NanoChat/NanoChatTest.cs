#nullable enable
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server._WF.NanoChat;
using Content.Server.Administration.Managers;
using Content.Server.CartridgeLoader;
using Content.Server.GameTicking;
using Content.Shared._WF.NanoChat;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared.PDA;
using Content.Shared.Players;
using Content.Shared.Power.EntitySystems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.NanoChat;

/// <summary>
/// Cards get distinct numbers, messages reach another PDA while a telecom server is up, sending is rate limited, and
/// ghosts read messages unless they are sent subtle.
/// </summary>
[TestFixture]
[TestOf(typeof(NanoChatCartridgeSystem))]
public sealed class NanoChatTest
{
    private const string Pda = "PassengerPDA";
    private const string IdCard = "PassengerIDCard";
    private const string Server = "TelecomServerFilledCommon";

    [Test]
    public async Task CardsGetDistinctNumbers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cards = Enumerable.Range(0, 40).Select(_ => entMan.SpawnEntity(IdCard, map.GridCoords)).ToList();
            var numbers = cards.Select(c => entMan.GetComponent<NanoChatCardComponent>(c).Number).ToList();

            Assert.That(numbers, Has.All.Not.Null);
            Assert.That(numbers, Is.Unique);
            Assert.That(numbers.Select(n => n!.Value), Has.All.InRange(1u, 9999u));

            // Giving one card another's number moves the other onto a free one.
            var nanoChat = entMan.System<NanoChatSystem>();
            var first = (cards[0], entMan.GetComponent<NanoChatCardComponent>(cards[0]));
            var taken = numbers[1]!.Value;
            nanoChat.AssignNumber(first, taken);
            Assert.That(first.Item2.Number, Is.EqualTo(taken));
            Assert.That(entMan.GetComponent<NanoChatCardComponent>(cards[1]).Number, Is.Not.EqualTo(taken));
        });

        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MessageNeedsSignalAndIsRateLimited()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid telecom = default;
        EntityUid pdaA = default;
        EntityUid pdaB = default;
        await server.WaitPost(() =>
        {
            telecom = entMan.SpawnEntity(Server, map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(telecom, false);
            pdaA = entMan.SpawnEntity(Pda, map.GridCoords);
            pdaB = entMan.SpawnEntity(Pda, map.GridCoords);
        });

        // The power net updates twice a second.
        await pair.RunTicksSync(90);

        Entity<NanoChatCardComponent> cardA = default;
        Entity<NanoChatCardComponent> cardB = default;
        EntityUid cartA = default;
        await server.WaitAssertion(() =>
        {
            cardA = CardOf(entMan, pdaA);
            cardB = CardOf(entMan, pdaB);
            Assert.That(cardA.Comp.PdaUid, Is.EqualTo(pdaA));
            Assert.That(entMan.System<CartridgeLoaderSystem>()
                .TryGetProgram<NanoChatCartridgeComponent>(pdaA, out var program, out _), "PDA has no NanoChat app.");
            cartA = program!.Value;

            Send(entMan, pdaA, cartA, cardB.Comp.Number!.Value, "hello");
            Send(entMan, pdaA, cartA, cardB.Comp.Number!.Value, "too soon");

            var sent = cardA.Comp.Messages[cardB.Comp.Number!.Value];
            Assert.That(sent, Has.Count.EqualTo(1), "The second message got past the rate limit.");
            Assert.That(sent[0].DeliveryFailed, Is.False, "The message failed with a telecom server up.");

            Assert.That(cardB.Comp.Messages.TryGetValue(cardA.Comp.Number!.Value, out var received), "Nothing arrived.");
            Assert.That(received!.Select(m => m.Content), Is.EqualTo(new[] { "hello" }));
            Assert.That(cardB.Comp.Recipients[cardA.Comp.Number!.Value].HasUnread, Is.True);

            entMan.DeleteEntity(telecom);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Send(entMan, pdaA, cartA, cardB.Comp!.Number!.Value, "anyone?");
            var sent = cardA.Comp!.Messages[cardB.Comp.Number!.Value];
            Assert.That(sent, Has.Count.EqualTo(2));
            Assert.That(sent[1].DeliveryFailed, Is.True, "A message went out with no telecom server.");
            Assert.That(cardB.Comp.Messages[cardA.Comp.Number!.Value], Has.Count.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A ghost reads a message in chat but not a subtle one, and an admin ghost reads the subtle one marked.</summary>
    [Test]
    public async Task GhostsSeeMessagesUnlessSubtle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var minds = entMan.System<SharedMindSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var session = server.PlayerMan.GetSessionById(client.Session!.UserId);
        var chat = client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();

        var wasAdmin = false;
        var pdas = new EntityUid[3];
        await server.WaitPost(() =>
        {
            var telecom = entMan.SpawnEntity(Server, map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(telecom, false);
            for (var i = 0; i < pdas.Length; i++)
            {
                pdas[i] = entMan.SpawnEntity(Pda, map.GridCoords);
            }

            var ghost = entMan.SpawnEntity(GameTicker.ObserverPrototypeName, map.GridCoords);
            minds.WipeMind(session.ContentData()?.Mind);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, ghost);

            wasAdmin = admins.IsAdmin(session);
            if (wasAdmin)
                admins.DeAdmin(session);
        });

        // The power net updates twice a second.
        await pair.RunTicksSync(90);

        void SendBetween(int from, int to, string text, bool subtle)
        {
            var app = entMan.System<CartridgeLoaderSystem>().TryGetProgram<NanoChatCartridgeComponent>(pdas[from], out var program, out _)
                ? program!.Value
                : throw new AssertionException("PDA has no NanoChat app.");
            Send(entMan, pdas[from], app, CardOf(entMan, pdas[to]).Comp.Number!.Value, text, subtle);
        }

        bool Seen(string text) => chat.History.Any(h => h.Msg.Message == text);

        await server.WaitPost(() =>
        {
            SendBetween(0, 1, "for everyone", false);
            SendBetween(1, 0, "between us", true);
        });
        await WaitFor(pair, () => Seen("for everyone"), "the ghost to read a message");
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var seen = chat.History.Last(h => h.Msg.Message == "for everyone").Msg;
            Assert.That(seen.Channel, Is.EqualTo(ChatChannel.Radio));
            Assert.That(seen.WrappedMessage, Does.Contain("[ColoTalk]"));
            Assert.That(Seen("between us"), Is.False, "A ghost read a subtle message.");
        });

        await server.WaitAssertion(() =>
        {
            // Both ends keep the subtle mark, which paints the message purple.
            var a = CardOf(entMan, pdas[0]).Comp;
            var b = CardOf(entMan, pdas[1]).Comp;
            Assert.That(a.Messages[b.Number!.Value].Last().Subtle, Is.True);
            Assert.That(b.Messages[a.Number!.Value].Last().Subtle, Is.True);
            Assert.That(b.Messages[a.Number!.Value].First().Subtle, Is.False);
        });

        await server.WaitPost(() =>
        {
            if (admins.IsAdmin(session, includeDeAdmin: true))
                admins.ReAdmin(session);
            else
                admins.PromoteHost(session);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() => SendBetween(2, 0, "admins only", true));
        await WaitFor(pair, () => Seen("admins only"), "an admin ghost to read a subtle message");
        await client.WaitAssertion(() =>
            Assert.That(chat.History.Last(h => h.Msg.Message == "admins only").Msg.WrappedMessage, Does.Contain("subtle")));

        await server.WaitPost(() =>
        {
            if (!wasAdmin)
                admins.DeAdmin(session);
            minds.WipeMind(session.ContentData()?.Mind);
        });
        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }

    private static async Task WaitFor(TestPair pair, Func<bool> condition, string what)
    {
        for (var i = 0; i < 60; i++)
        {
            if (condition())
                return;

            await pair.RunTicksSync(1);
        }

        Assert.That(condition(), $"Timed out waiting for {what}.");
    }

    private static Entity<NanoChatCardComponent> CardOf(IEntityManager entMan, EntityUid pda)
    {
        var id = entMan.GetComponent<PdaComponent>(pda).ContainedId;
        Assert.That(id, Is.Not.Null, "PDA spawned without an ID.");
        return (id!.Value, entMan.GetComponent<NanoChatCardComponent>(id.Value));
    }

    private static void Send(IEntityManager entMan, EntityUid pda, EntityUid cartridge, uint number, string text, bool subtle = false)
    {
        var msg = new NanoChatUiMessageEvent(NanoChatUiMessageType.SendMessage, number, text, subtle: subtle)
        {
            LoaderUid = entMan.GetNetEntity(pda),
            Actor = pda,
            User = pda,
        };
        entMan.EventBus.RaiseLocalEvent<CartridgeMessageEvent>(cartridge, msg);
    }
}
