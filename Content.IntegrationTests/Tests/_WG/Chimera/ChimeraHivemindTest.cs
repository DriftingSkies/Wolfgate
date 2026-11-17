#nullable enable
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Chimera;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using ClientChatManager = Content.Client.Chat.Managers.IChatManager;

namespace Content.IntegrationTests.Tests._WG.Chimera;

/// <summary>
/// A player in a fleshbeast is told how to talk, hears the Letoferol hivemind before speaking in it, and reaches it
/// from the channel selector and with a + prefix.
/// </summary>
[TestFixture]
[TestOf(typeof(MindGreetingSystem))]
public sealed class ChimeraHivemindTest
{
    private const string BeastProto = "MobLetoferolHorror";
    private const string Greeting = "wg-chimera-greeting";
    private const int MaxWaitTicks = 120;

    [Test]
    public async Task ChimerasReachTheHivemind()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var chatSys = entMan.System<ChatSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientChat = pair.Client.ResolveDependency<ClientChatManager>();
        var greeting = server.ResolveDependency<ILocalizationManager>().GetString(Greeting);
        var other = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            var beast = entMan.SpawnEntity(BeastProto, map.GridCoords);
            other = entMan.SpawnEntity(BeastProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, beast);
        });

        await WaitFor(pair, () => chat.History.Any(h => h.Msg.Channel == ChatChannel.Server && h.Msg.Message == greeting),
            "the player to be told how to talk as a chimera");

        // The player hasn't said anything yet.
        await server.WaitPost(() => chatSys.TrySendInGameICMessage(other,
            "+ can you hear me",
            InGameICChatType.CollectiveMind,
            ChatTransmitRange.Normal));
        await WaitForMind(pair, chat, "Can you hear me", "a chimera that hasn't spoken to hear the hivemind");

        // The channel selector sends without a prefix.
        await pair.Client.WaitPost(() => clientChat.SendMessage("from the selector", ChatSelectChannel.CollectiveMind));
        await WaitForMind(pair, chat, "From the selector", "a message from the channel selector");

        // Letters that are other minds' keys, straight after the +.
        await pair.Client.WaitPost(() => clientChat.SendMessage("+run", ChatSelectChannel.CollectiveMind));
        await WaitForMind(pair, chat, "Run", "\"+run\", whose r is the borer mind's key");

        await pair.Client.WaitPost(() => clientChat.SendMessage("+look", ChatSelectChannel.CollectiveMind));
        await WaitForMind(pair, chat, "Look", "\"+look\", whose l is the Letoferol key");

        await pair.Client.WaitPost(() => clientChat.SendMessage("+l keyed", ChatSelectChannel.CollectiveMind));
        await WaitForMind(pair, chat, "Keyed", "\"+l keyed\" with an explicit key");

        Assert.That(chat.History.Any(h => h.Msg.Channel == ChatChannel.CollectiveMind && h.Msg.Message.StartsWith("Ook")),
            Is.False,
            "\"+look\" lost its first letter to the key.");

        await pair.CleanReturnAsync();
    }

    private static async Task WaitForMind(TestPair pair, ChatUIController chat, string start, string what)
    {
        await WaitFor(pair,
            () => chat.History.Any(h => h.Msg.Channel == ChatChannel.CollectiveMind && h.Msg.Message.StartsWith(start)),
            what);
    }

    private static async Task WaitFor(TestPair pair, Func<bool> condition, string what)
    {
        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (condition())
                return;

            await pair.RunTicksSync(1);
        }

        Assert.That(condition(), Is.True, $"Timed out waiting for {what}.");
    }
}
