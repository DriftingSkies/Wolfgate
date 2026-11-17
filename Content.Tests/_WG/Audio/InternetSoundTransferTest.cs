using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.Audio.InternetSound;
using NUnit.Framework;

namespace Content.Tests._WG.Audio;

[TestFixture]
public sealed class InternetSoundTransferTest
{
    [Test]
    public async Task CutDropsPayloadAndSerializesReplacementBehindStalledWrite()
    {
        var queue = new InternetSoundTransferQueue();
        var client = new object();
        using var first = new InternetSoundTransfer(new byte[InternetSoundTransferQueue.ChunkSize * 4]);
        using var replacement = new InternetSoundTransfer(new byte[] { 1, 2, 3 });
        var blocked = new BlockedStream();
        var firstSend = queue.SendAsync(client, first, () => Task.FromResult<Stream>(blocked));
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Dispose();
        Assert.That(first.CopyChunk(0, new byte[1]), Is.Zero, "Cut releases the complete payload even during an uninterruptible write.");
        var opened = false;
        var output = new MemoryStream();
        var nextSend = queue.SendAsync(client, replacement, () =>
        {
            opened = true;
            return Task.FromResult<Stream>(output);
        });
        Assert.That(opened, Is.False, "A slow client must not accumulate simultaneous outgoing streams.");
        blocked.Continue.TrySetResult();
        Assert.CatchAsync<OperationCanceledException>(async () => await firstSend.WaitAsync(TimeSpan.FromSeconds(5)));
        await nextSend.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(blocked.Length, Is.EqualTo(InternetSoundTransferQueue.ChunkSize));
        Assert.That(output.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public async Task CanceledQueuedTransferNeverOpensAStream()
    {
        var queue = new InternetSoundTransferQueue();
        var client = new object();
        using var first = new InternetSoundTransfer(new byte[1]);
        using var queued = new InternetSoundTransfer(new byte[1]);
        var blocked = new BlockedStream();
        var firstSend = queue.SendAsync(client, first, () => Task.FromResult<Stream>(blocked));
        await blocked.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var opened = false;
        var queuedSend = queue.SendAsync(client, queued, () =>
        {
            opened = true;
            return Task.FromResult<Stream>(new MemoryStream());
        });
        queued.Dispose();
        Assert.CatchAsync<OperationCanceledException>(async () => await queuedSend.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(opened, Is.False);
        blocked.Continue.TrySetResult();
        await firstSend.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class BlockedStream : MemoryStream
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            // Model an engine socket wait that does not observe cancellation.
            await Continue.Task;
            Write(buffer.Span);
        }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
