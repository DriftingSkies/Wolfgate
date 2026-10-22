#nullable enable
using System.Linq;
using Content.Shared._WF.MappingMode;
using NUnit.Framework;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingUploadAssemblerTest
{
    private const int Chunk = WFMappingUploadAssembler.ChunkSize;

    private static byte[] File(int bytes)
    {
        return Enumerable.Range(0, bytes).Select(i => (byte) (i * 7 + i / 251)).ToArray();
    }

    private static byte[] Slice(byte[] file, int index)
    {
        return file.Skip(index * Chunk).Take(Chunk).ToArray();
    }

    private static WFMappingUploadAssembler Create(int bytes)
    {
        Assert.That(WFMappingUploadAssembler.TryCreate(5, bytes, WFMappingUploadAssembler.ChunksFor(bytes),
            WFMappingUploadAssembler.MaxFileBytes, out var assembler), Is.True);
        return assembler!;
    }

    [Test]
    public void InOrder()
    {
        var file = File(Chunk * 2 + 100);
        var assembler = Create(file.Length);
        Assert.That(assembler.ChunkCount, Is.EqualTo(3));
        for (var i = 0; i < 3; i++)
        {
            Assert.That(assembler.TryComplete(out _), Is.False, "Not every chunk is in yet.");
            Assert.That(assembler.Add(i, Slice(file, i)), Is.EqualTo(WFMappingChunkResult.Accepted));
        }

        Assert.Multiple(() =>
        {
            Assert.That(assembler.IsComplete, Is.True);
            Assert.That(assembler.TryComplete(out var data), Is.True);
            Assert.That(data, Is.EqualTo(file));
        });
    }

    [Test]
    public void OutOfOrder()
    {
        var file = File(Chunk * 3 + 1);
        var assembler = Create(file.Length);
        foreach (var i in new[] { 3, 1, 0, 2 })
        {
            Assert.That(assembler.Add(i, Slice(file, i)), Is.EqualTo(WFMappingChunkResult.Accepted));
        }

        Assert.That(assembler.TryComplete(out var data), Is.True);
        Assert.That(data, Is.EqualTo(file));
    }

    [Test]
    public void Duplicate()
    {
        var file = File(Chunk + 10);
        var assembler = Create(file.Length);
        Assert.Multiple(() =>
        {
            Assert.That(assembler.Add(0, Slice(file, 0)), Is.EqualTo(WFMappingChunkResult.Accepted));
            Assert.That(assembler.Add(0, Slice(file, 0)), Is.EqualTo(WFMappingChunkResult.Duplicate));
            Assert.That(assembler.Received, Is.EqualTo(1));
        });
    }

    [Test]
    public void OutOfRange()
    {
        var assembler = Create(Chunk + 10);
        Assert.Multiple(() =>
        {
            Assert.That(assembler.Add(-1, new byte[1]), Is.EqualTo(WFMappingChunkResult.OutOfRange));
            Assert.That(assembler.Add(2, new byte[1]), Is.EqualTo(WFMappingChunkResult.OutOfRange));
            Assert.That(assembler.Received, Is.EqualTo(0));
        });
    }

    [Test]
    public void ChunkOverSize()
    {
        var assembler = Create(Chunk * 2);
        Assert.That(assembler.Add(0, new byte[Chunk + 1]), Is.EqualTo(WFMappingChunkResult.TooLarge));
    }

    [Test]
    public void TotalOverTheAnnouncedSize()
    {
        var assembler = Create(Chunk + 10);
        Assert.Multiple(() =>
        {
            Assert.That(assembler.Add(0, new byte[Chunk]), Is.EqualTo(WFMappingChunkResult.Accepted));
            Assert.That(assembler.Add(1, new byte[11]), Is.EqualTo(WFMappingChunkResult.TooLarge));
        });
    }

    [Test]
    public void ShortChunksNeverComplete()
    {
        var assembler = Create(Chunk + 10);
        assembler.Add(0, new byte[Chunk - 1]);
        assembler.Add(1, new byte[10]);
        Assert.Multiple(() =>
        {
            Assert.That(assembler.IsComplete, Is.True);
            Assert.That(assembler.TryComplete(out _), Is.False, "The chunks don't add up to the announced size.");
        });
    }

    [Test]
    public void TotalOverTheCap()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingUploadAssembler.TryCreate(1, 1001, 1, 1000, out _), Is.False);
            Assert.That(WFMappingUploadAssembler.TryCreate(1, 1000, 1, 1000, out _), Is.True);
            var over = WFMappingUploadAssembler.MaxFileBytes + 1L;
            Assert.That(WFMappingUploadAssembler.TryCreate(1, over, WFMappingUploadAssembler.ChunksFor(over), long.MaxValue, out _),
                Is.False, "The hard cap holds whatever the cvar says.");
            Assert.That(WFMappingUploadAssembler.TryCreate(1, 0, 1, 1000, out _), Is.False, "An empty file.");
            Assert.That(WFMappingUploadAssembler.Cap(0), Is.EqualTo(1));
            Assert.That(WFMappingUploadAssembler.Cap(long.MaxValue), Is.EqualTo(WFMappingUploadAssembler.MaxFileBytes));
        });
    }

    [Test]
    public void MismatchedCount()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WFMappingUploadAssembler.TryCreate(1, Chunk + 1, 1, WFMappingUploadAssembler.MaxFileBytes, out _), Is.False);
            Assert.That(WFMappingUploadAssembler.TryCreate(1, Chunk + 1, 3, WFMappingUploadAssembler.MaxFileBytes, out _), Is.False);
            Assert.That(WFMappingUploadAssembler.TryCreate(1, Chunk, 1, WFMappingUploadAssembler.MaxFileBytes, out _), Is.True);
            Assert.That(WFMappingUploadAssembler.ChunksFor(WFMappingUploadAssembler.MaxFileBytes),
                Is.EqualTo(WFMappingUploadAssembler.MaxChunks));
        });
    }
}
