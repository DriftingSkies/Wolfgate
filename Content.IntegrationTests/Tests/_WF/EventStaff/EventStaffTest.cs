#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.Administration.Managers;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared._WF.EventStaff;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using ClientBwoinkSystem = Content.Client.Administration.Systems.BwoinkSystem;
using ServerBwoinkSystem = Content.Server.Administration.Systems.BwoinkSystem;

namespace Content.IntegrationTests.Tests._WF.EventStaff;

/// <summary>
/// An admin with only the Event flag gets event chat and can open and answer gameplay ahelps.
/// </summary>
[TestFixture]
public sealed class EventStaffTest
{
    private const int MaxWaitTicks = 120;

    [Test]
    public async Task EventStaffChatAndGameplayAHelp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true, Fresh = true });
        var server = pair.Server;
        var client = pair.Client;
        var db = server.ResolveDependency<IServerDbManager>();
        var admins = server.ResolveDependency<IAdminManager>();
        var clientAdmins = client.ResolveDependency<IClientAdminManager>();
        var chat = client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        var clientConsole = client.ResolveDependency<IConsoleHost>();
        var userId = client.Session!.UserId;
        var session = server.PlayerMan.GetSessionById(userId);

        await db.AddAdminAsync(new Admin
        {
            UserId = userId.UserId,
            Flags = new List<AdminFlag> { new() { Flag = "EVENT" } },
        });
        // The local test client is otherwise promoted to host with every flag.
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.ConsoleLoginLocal, false);
            admins.ReloadAdmin(session);
        });
        await WaitFor(pair, () => clientAdmins.HasFlag(AdminFlags.Event) && !clientAdmins.HasFlag(AdminFlags.Adminhelp),
            "the client to have only the Event flag");

        Assert.Multiple(() =>
        {
            Assert.That(admins.HasAdminFlag(session, AdminFlags.Event));
            Assert.That(admins.HasAdminFlag(session, AdminFlags.Adminhelp), Is.False);
            Assert.That(chat.FilterableChannels.HasFlag(ChatChannel.EventChat));
            Assert.That(chat.CanSendChannels.HasFlag(ChatSelectChannel.Event));
            Assert.That(chat.CanSendChannels.HasFlag(ChatSelectChannel.Admin), Is.False);
        });

        // Event chat reaches the sender.
        await client.WaitPost(() => clientConsole.ExecuteCommand("esay hello event staff"));
        await WaitFor(pair,
            () => chat.History.Any(h => h.Msg.Channel == ChatChannel.EventChat && h.Msg.Message == "hello event staff"),
            "the event chat message");

        // Event staff may open an ahelp with a player who has none; it becomes a gameplay one and comes back to them.
        var received = new List<ClientBwoinkSystem.BwoinkTextMessage>();
        var clientBwoink = client.System<ClientBwoinkSystem>();
        EventHandler<ClientBwoinkSystem.BwoinkTextMessage> onReceived = (_, msg) => received.Add(msg);
        clientBwoink.OnBwoinkTextMessageRecieved += onReceived;

        var channel = new NetUserId(Guid.NewGuid());
        await client.WaitPost(() => clientBwoink.Send(channel, "need a hand?", true, false));
        await WaitFor(pair, () => received.Any(m => m.UserId == channel), "the event staff ahelp to come back");

        var serverBwoink = server.System<ServerBwoinkSystem>();
        Assert.Multiple(() =>
        {
            Assert.That(serverBwoink.GetAHelpCategory(channel), Is.EqualTo(AHelpCategory.Gameplay));
            var line = received.First(m => m.UserId == channel);
            Assert.That(line.Category, Is.EqualTo(AHelpCategory.Gameplay));
            Assert.That(line.Text, Does.Contain("need a hand?"));
        });

        // The player's own ahelp keeps the category they picked.
        await client.WaitPost(() => clientBwoink.Send(userId, "a player is griefing", true, false, AHelpCategory.Player));
        await WaitFor(pair, () => received.Any(m => m.UserId == userId), "the player's own ahelp");
        Assert.That(serverBwoink.GetAHelpCategory(userId), Is.EqualTo(AHelpCategory.Player));

        clientBwoink.OnBwoinkTextMessageRecieved -= onReceived;
        await db.RemoveAdminAsync(userId);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.ConsoleLoginLocal, true);
            admins.ReloadAdmin(session);
        });
        await WaitFor(pair, () => clientAdmins.HasFlag(AdminFlags.Host), "the client to be host again");

        await pair.CleanReturnAsync();
    }

    private static async Task WaitFor(TestPair pair, Func<bool> condition, string what)
    {
        for (var i = 0; i < MaxWaitTicks && !condition(); i++)
        {
            await pair.RunTicksSync(1);
        }

        Assert.That(condition(), $"Timed out waiting for {what}.");
    }
}
