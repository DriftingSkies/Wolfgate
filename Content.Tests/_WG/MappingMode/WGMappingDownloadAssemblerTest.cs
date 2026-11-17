using System;
using System.Linq;
using Content.Shared._WG.MappingMode;
using NUnit.Framework;

namespace Content.Tests._WG.MappingMode;

[TestFixture]
public sealed class WGMappingDownloadAssemblerTest
{
    private static byte[][] Chunks(int count, int size)
    {
        return Enumerable.Range(0, count)
            .Select(i => Enumerable.Range(0, size).Select(b => (byte) (i * 31 + b)).ToArray())
            .ToArray();
    }

    private static WGMappingDownloadAssembler Create(int chunkCount, int totalBytes)
    {
        Assert.That(WGMappingDownloadAssembler.TryCreate(1, "Map", "Map.yml", chunkCount, totalBytes, out var assembler),
            Is.True);
        return assembler!;
    }

    [TestCase("My Map.yml", 1, true)]
    [TestCase("My Map_z0.yml", 1, true)]
    [TestCase("My Map_z-2.yml", 3, true)]
    [TestCase("My Map.vessel.yml", 1, true)]
    [TestCase("My Map.yml", 0, false)]
    [TestCase("My Map.yml", WGMappingDownloadAssembler.MaxChunks + 1, false)]
    [TestCase("z0.yml", 1, false)]
    [TestCase("../My Map.yml", 1, false)]
    [TestCase("My Map_z0.grid.yml", 1, false)]
    [TestCase("My Map_z01.yml", 1, false)]
    [TestCase("My Map/../x.yml", 1, false)]
    [TestCase("Other.yml", 1, false)]
    public void TransfersCheckNameAndCount(string name, int chunks, bool valid)
    {
        Assert.That(WGMappingDownloadAssembler.TryCreate(1, "My Map", name, chunks, 100, out _), Is.EqualTo(valid));
    }

    [TestCase(0, false)]
    [TestCase(-1, false)]
    [TestCase(1, true)]
    [TestCase(WGMappingDownloadAssembler.MaxFileBytes, true)]
    [TestCase(WGMappingDownloadAssembler.MaxFileBytes + 1, false)]
    public void TransfersCheckTheAnnouncedSize(int bytes, bool valid)
    {
        Assert.That(WGMappingDownloadAssembler.TryCreate(1, "Map", "Map.yml", 1, bytes, out _), Is.EqualTo(valid));
    }

    [TestCase(new[] { 0, 1, 2 })]
    [TestCase(new[] { 2, 0, 1 })]
    [TestCase(new[] { 1, 2, 0 })]
    public void ChunksInAnyOrderMakeTheFile(int[] order)
    {
        var chunks = Chunks(3, 10);
        var assembler = Create(3, 30);
        foreach (var index in order)
        {
            Assert.That(assembler.TryComplete(out _), Is.False);
            Assert.That(assembler.Add(index, 3, chunks[index]), Is.EqualTo(WGMappingChunkResult.Accepted));
        }

        Assert.That(assembler.IsComplete, Is.True);
        Assert.That(assembler.TryComplete(out var data), Is.True);
        Assert.That(data, Is.EqualTo(chunks.SelectMany(c => c).ToArray()));
    }

    [Test]
    public void DuplicatesAndOutOfRangeAreRefused()
    {
        var chunks = Chunks(2, 4);
        var assembler = Create(2, 8);
        Assert.Multiple(() =>
        {
            Assert.That(assembler.Add(0, 2, chunks[0]), Is.EqualTo(WGMappingChunkResult.Accepted));
            Assert.That(assembler.Add(0, 2, chunks[1]), Is.EqualTo(WGMappingChunkResult.Duplicate));
            Assert.That(assembler.Add(2, 2, chunks[1]), Is.EqualTo(WGMappingChunkResult.OutOfRange));
            Assert.That(assembler.Add(-1, 2, chunks[1]), Is.EqualTo(WGMappingChunkResult.OutOfRange));
            Assert.That(assembler.Add(1, 3, chunks[1]), Is.EqualTo(WGMappingChunkResult.OutOfRange));
            Assert.That(assembler.Received, Is.EqualTo(1));
            Assert.That(assembler.IsComplete, Is.False);
        });

        Assert.That(assembler.Add(1, 2, chunks[1]), Is.EqualTo(WGMappingChunkResult.Accepted));
        Assert.That(assembler.TryComplete(out var data), Is.True);
        Assert.That(data, Is.EqualTo(chunks[0].Concat(chunks[1]).ToArray()), "The duplicate didn't replace the first chunk.");
    }

    [Test]
    public void OversizeChunksAreRefused()
    {
        var assembler = Create(2, WGMappingDownloadAssembler.MaxFileBytes);
        Assert.That(assembler.Add(0, 2, new byte[WGMappingDownloadAssembler.ChunkSize + 1]),
            Is.EqualTo(WGMappingChunkResult.TooLarge));
        Assert.That(assembler.Received, Is.Zero);
    }

    [Test]
    public void AFileOfExactlyTheCapCompletes()
    {
        var chunk = new byte[WGMappingDownloadAssembler.ChunkSize];
        chunk[0] = 7;
        var assembler = Create(WGMappingDownloadAssembler.MaxChunks, WGMappingDownloadAssembler.MaxFileBytes);
        for (var i = 0; i < WGMappingDownloadAssembler.MaxChunks; i++)
        {
            Assert.That(assembler.Add(i, WGMappingDownloadAssembler.MaxChunks, chunk), Is.EqualTo(WGMappingChunkResult.Accepted));
        }

        Assert.That(assembler.TryComplete(out var data), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(data, Has.Length.EqualTo(WGMappingDownloadAssembler.MaxFileBytes));
            Assert.That(data[WGMappingDownloadAssembler.ChunkSize], Is.EqualTo(7));
        });
    }

    [Test]
    public void AnEmptyFileIsRefused()
    {
        Assert.That(WGMappingDownloadAssembler.TryCreate(1, "Map", "Map.yml", 1, 0, out _), Is.False);
        Assert.That(WGMappingDownloadAssembler.ChunksFor(0), Is.EqualTo(1));
    }

    [Test]
    public void ChunksMustAddUpToTheAnnouncedSize()
    {
        var chunks = Chunks(2, 4);
        var shortFile = Create(2, 10);
        Assert.That(shortFile.Add(0, 2, chunks[0]), Is.EqualTo(WGMappingChunkResult.Accepted));
        Assert.That(shortFile.Add(1, 2, chunks[1]), Is.EqualTo(WGMappingChunkResult.Accepted));
        Assert.Multiple(() =>
        {
            Assert.That(shortFile.IsComplete, Is.True);
            Assert.That(shortFile.TryComplete(out var data), Is.False, "Eight bytes of an announced ten aren't a file.");
            Assert.That(data, Is.Empty);
        });

        var longFile = Create(2, 6);
        Assert.That(longFile.Add(0, 2, chunks[0]), Is.EqualTo(WGMappingChunkResult.Accepted));
        Assert.That(longFile.Add(1, 2, chunks[1]), Is.EqualTo(WGMappingChunkResult.TooLarge));
    }

    [TestCase(1, 1)]
    [TestCase(WGMappingDownloadAssembler.ChunkSize, 1)]
    [TestCase(WGMappingDownloadAssembler.ChunkSize + 1, 2)]
    public void ChunkCounts(int bytes, int chunks)
    {
        Assert.That(WGMappingDownloadAssembler.ChunksFor(bytes), Is.EqualTo(chunks));
    }
}
