#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client.Chat.Managers;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Subtle;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.Subtle;

/// <summary>
/// A subtle emote reaches the sender and the players next to it, and nobody further away.
/// </summary>
[TestFixture]
[TestOf(typeof(SubtleSystem))]
public sealed class SubtleEmoteTest
{
    private const string MobProto = "MobHuman";
    private const int MaxWaitTicks = 120;

    [Test]
    public async Task SubtleEmotesStayAdjacent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var xformSys = entMan.System<SharedTransformSystem>();
        var subtle = entMan.System<SubtleSystem>();
        var verbs = entMan.System<SharedVerbSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientConsole = pair.Client.ResolveDependency<IConsoleHost>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();
        var listener = EntityUid.Invalid;
        var other = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            listener = entMan.SpawnEntity(MobProto, map.GridCoords);
            other = entMan.SpawnEntity(MobProto, map.GridCoords.Offset(new Vector2(1f, 1f)));
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, listener);
        });
        await pair.RunTicksSync(5);

        // The verb is on the player's own body only.
        var verbText = server.ResolveDependency<ILocalizationManager>().GetString("wf-subtle-verb");
        await server.WaitAssertion(() =>
        {
            Assert.That(verbs.GetLocalVerbs(listener, listener, typeof(Verb)).Any(v => v.Text == verbText), Is.True);
            Assert.That(verbs.GetLocalVerbs(other, listener, typeof(Verb)).Any(v => v.Text == verbText), Is.False);
        });

        // Diagonally adjacent.
        await server.WaitAssertion(() =>
        {
            Assert.That(subtle.InSubtleRange(other, listener), Is.True);
            Assert.That(subtle.GetRecipients(other), Is.EquivalentTo(new[] { session }));
            Assert.That(subtle.TrySendSubtle(other, "leans in close"), Is.True);
        });
        await WaitForEmote(pair, chat, "leans in close", "an adjacent player to see a subtle emote");

        // Two tiles away.
        await server.WaitAssertion(() =>
        {
            xformSys.SetCoordinates(other, map.GridCoords.Offset(new Vector2(2f, 0f)));
            Assert.That(subtle.InSubtleRange(other, listener), Is.False);
            Assert.That(subtle.GetRecipients(other), Is.Empty);
            Assert.That(subtle.TrySendSubtle(other, "mutters from afar"), Is.True);
        });

        // The player's own emote comes back to them, through the command and the chat limits.
        await pair.Client.WaitPost(() => clientConsole.ExecuteCommand("subtle taps the table"));
        await WaitForEmote(pair, chat, "taps the table", "the sender to see their own subtle emote");

        // The Subtle chat channel sends the same way.
        await pair.Client.WaitPost(() => clientChat.SendMessage("nods slowly", ChatSelectChannel.Subtle));
        await WaitForEmote(pair, chat, "nods slowly", "a subtle emote sent from the chat channel");

        Assert.That(chat.History.Any(h => h.Msg.Message.Contains("mutters from afar")),
            Is.False,
            "A subtle emote reached a player who was not adjacent.");

        await pair.CleanReturnAsync();
    }

    private static async Task WaitForEmote(TestPair pair, ChatUIController chat, string text, string what)
    {
        bool Arrived() => chat.History.Any(h => h.Msg.Channel == ChatChannel.Subtle && h.Msg.Message == text);

        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (Arrived())
                return;

            await pair.RunTicksSync(1);
        }

        Assert.That(Arrived(), Is.True, $"Timed out waiting for {what}.");
    }
}
