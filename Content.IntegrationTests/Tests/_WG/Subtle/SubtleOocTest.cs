#nullable enable
using System.Linq;
using Content.Client.Chat.Managers;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server._WG.Subtle;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Client.UserInterface;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Subtle;

/// <summary>
/// SOOC sent from the chat channel or the command comes back to the sender on its own channel, and follows the LOOC
/// toggles.
/// </summary>
[TestFixture]
[TestOf(typeof(SubtleOocSystem))]
public sealed class SubtleOocTest
{
    private const string MobProto = "MobHuman";
    private const int MaxWaitTicks = 120;

    [Test]
    public async Task SubtleOocReachesSender()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var subtleOoc = entMan.System<SubtleOocSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientConsole = pair.Client.ResolveDependency<IConsoleHost>();
        var clientChat = pair.Client.ResolveDependency<IChatManager>();

        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            var body = entMan.SpawnEntity(MobProto, map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(session.UserId).Owner, body);
        });
        await pair.RunTicksSync(5);

        await pair.Client.WaitPost(() => clientChat.SendMessage("brb, door", ChatSelectChannel.SubtleOOC));
        await WaitFor(pair, chat, "brb, door", "SOOC sent from the chat channel");

        await pair.Client.WaitPost(() => clientConsole.ExecuteCommand("sooc is this okay?"));
        await WaitFor(pair, chat, "is this okay?", "SOOC sent from the command");

        // Off when LOOC is off.
        await server.WaitAssertion(() =>
        {
            pair.Server.CfgMan.SetCVar(CCVars.LoocEnabled, false);
            pair.Server.CfgMan.SetCVar(CCVars.AdminLoocEnabled, false);
            Assert.That(subtleOoc.TrySendSubtleOoc(session, "should not send"), Is.False);
            pair.Server.CfgMan.SetCVar(CCVars.LoocEnabled, true);
            pair.Server.CfgMan.SetCVar(CCVars.AdminLoocEnabled, true);
        });

        await pair.CleanReturnAsync();
    }

    private static async Task WaitFor(TestPair pair, ChatUIController chat, string text, string what)
    {
        bool Arrived() => chat.History.Any(h => h.Msg.Channel == ChatChannel.SubtleOOC && h.Msg.Message == text);

        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (Arrived())
                return;

            await pair.RunTicksSync(1);
        }

        Assert.That(Arrived(), Is.True, $"Timed out waiting for {what}.");
    }
}
