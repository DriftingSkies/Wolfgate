using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._WG.ProfileSaves;
using NUnit.Framework;
using Robust.Shared.Network;

namespace Content.Tests._WG.ProfileSaves;

/// <summary>Checks a player's writes never overlap and run in order, without holding up other players.</summary>
[TestFixture]
[TestOf(typeof(WGUserWriteQueue))]
public sealed class WGUserWriteQueueTest
{
    private static readonly NetUserId Alice = new(Guid.NewGuid());
    private static readonly NetUserId Bob = new(Guid.NewGuid());

    [Test]
    public async Task SamePlayerRunsInOrderWithoutOverlap()
    {
        var queue = new WGUserWriteQueue();
        var first = new TaskCompletionSource();
        var ran = new List<int>();

        var a = queue.Run(Alice, async () =>
        {
            ran.Add(1);
            await first.Task;
            ran.Add(2);
        });
        var b = queue.Run(Alice, () =>
        {
            ran.Add(3);
            return Task.CompletedTask;
        });
        var c = queue.Run(Alice, () =>
        {
            ran.Add(4);
            return Task.CompletedTask;
        });

        await Task.Delay(50);
        Assert.That(ran, Is.EqualTo(new[] { 1 }), "Later writes must wait for the one in progress.");

        first.SetResult();
        await Task.WhenAll(a, b, c);
        Assert.That(ran, Is.EqualTo(new[] { 1, 2, 3, 4 }));
    }

    [Test]
    public async Task OtherPlayersAreNotHeldUp()
    {
        var queue = new WGUserWriteQueue();
        var blocked = new TaskCompletionSource();
        var aliceWrite = queue.Run(Alice, () => blocked.Task);

        var bobRan = false;
        await queue.Run(Bob, () =>
        {
            bobRan = true;
            return Task.CompletedTask;
        });

        Assert.That(bobRan, Is.True);
        Assert.That(aliceWrite.IsCompleted, Is.False);
        blocked.SetResult();
        await aliceWrite;
    }

    [Test]
    public async Task FailureReachesOnlyItsCaller()
    {
        var queue = new WGUserWriteQueue();
        var failing = queue.Run(Alice, () => throw new InvalidOperationException("save failed"));

        var ran = false;
        await queue.Run(Alice, () =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.That(ran, Is.True, "A failed write must not block the next one.");
        Assert.ThrowsAsync<InvalidOperationException>(async () => await failing);
    }
}
