using System.Collections.Generic;
using Content.Server._WG.Performance;
using Content.Shared._Mono.Radar;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>Checks a message a client sends is timed under its own type.</summary>
[TestFixture]
[TestOf(typeof(WGNetDispatchWatchSystem))]
public sealed class WGNetDispatchWatchTest
{
    [Test]
    public async Task ClientMessageIsChargedToItsType()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var watch = server.System<WGNetDispatchWatchSystem>();
        const string label = nameof(RequestBlipsEvent);

        var before = 0;
        await server.WaitPost(() => before = watch.Totals.GetValueOrDefault(label).Count);

        // An invalid radar makes the server's handler return straight away.
        for (var i = 0; i < 3; i++)
        {
            await client.WaitPost(() =>
                client.EntMan.EntityNetManager!.SendSystemNetworkMessage(new RequestBlipsEvent(NetEntity.Invalid)));
            await pair.RunTicksSync(2);
        }

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var total = watch.Totals.GetValueOrDefault(label);
            Assert.That(total.Count - before, Is.EqualTo(3), "Each message is charged once, under its own type.");
            Assert.That(total.Seconds, Is.GreaterThanOrEqualTo(0));
        });

        await pair.CleanReturnAsync();
    }
}
