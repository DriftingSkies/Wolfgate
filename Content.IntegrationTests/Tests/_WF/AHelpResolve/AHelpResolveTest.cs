#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Administration;
using Robust.Shared.GameObjects;
using ClientBwoinkSystem = Content.Client.Administration.Systems.BwoinkSystem;
using ServerBwoinkSystem = Content.Server.Administration.Systems.BwoinkSystem;

namespace Content.IntegrationTests.Tests._WF.AHelpResolve;

/// <summary>
/// A player's ahelp bwoinks staff until a staffer answers, stays quiet until resolved, then bwoinks again.
/// </summary>
[TestFixture]
public sealed class AHelpResolveTest
{
    private const int MaxWaitTicks = 120;

    [Test]
    public async Task AnsweredAHelpStopsBwoinkingUntilResolved()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var userId = client.Session!.UserId;

        // The test client is host, so it gets the staff copy of its own ahelp.
        var received = new List<ClientBwoinkSystem.BwoinkTextMessage>();
        var clientBwoink = client.System<ClientBwoinkSystem>();
        var serverBwoink = server.System<ServerBwoinkSystem>();
        EventHandler<ClientBwoinkSystem.BwoinkTextMessage> onReceived = (_, msg) => received.Add(msg);
        clientBwoink.OnBwoinkTextMessageRecieved += onReceived;

        var first = await Send(pair, clientBwoink, received, "first");
        Assert.That(first.PlaySound, "An unanswered ahelp should bwoink staff.");

        await server.WaitPost(() => serverBwoink.OnWebhookBwoinkTextMessage(
            new ClientBwoinkSystem.BwoinkTextMessage(userId, ClientBwoinkSystem.SystemUserId, "staff reply"),
            new ServerApi.BwoinkActionBody { Text = "staff reply", Username = "Staff", Guid = userId.UserId, WebhookUpdate = false }));
        await WaitFor(pair, () => received.Any(m => m.Text.Contains("staff reply")), "the staff reply");
        Assert.Multiple(() =>
        {
            Assert.That(received.First(m => m.Text.Contains("staff reply")).PlaySound, Is.False, "A staff reply should not bwoink other staff.");
            Assert.That(serverBwoink.IsAHelpClaimed(userId));
        });

        var second = await Send(pair, clientBwoink, received, "second");
        Assert.That(second.PlaySound, Is.False, "An answered ahelp should not bwoink staff.");

        var before = received.Count;
        await client.WaitPost(() => clientBwoink.SendResolve(userId));
        await WaitFor(pair, () => received.Count > before, "the resolve notice");
        Assert.That(serverBwoink.IsAHelpClaimed(userId), Is.False);

        var third = await Send(pair, clientBwoink, received, "third");
        Assert.That(third.PlaySound, "A resolved ahelp should bwoink staff again.");

        clientBwoink.OnBwoinkTextMessageRecieved -= onReceived;
        await pair.CleanReturnAsync();
    }

    private static async Task<ClientBwoinkSystem.BwoinkTextMessage> Send(TestPair pair, ClientBwoinkSystem bwoink,
        List<ClientBwoinkSystem.BwoinkTextMessage> received, string text)
    {
        var userId = pair.Client.Session!.UserId;
        await pair.Client.WaitPost(() => bwoink.Send(userId, text, true, false));
        await WaitFor(pair, () => received.Any(m => m.Text.EndsWith($": {text}")), $"the \"{text}\" ahelp");
        return received.First(m => m.Text.EndsWith($": {text}"));
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
