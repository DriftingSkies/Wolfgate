using System;
using System.Linq;
using Content.Shared._WF.MappingMode;
using NUnit.Framework;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingDownloadAssemblerTest
{
    private static byte[][] Chunks(int count, int size)
    {
        return Enumerable.Range(0, count)
            .Select(i => Enumerable.Range(0, size).Select(b => (byte) (i * 31 + b)).ToArray())
            .ToArray();
    }

    private static WFMappingDownloadAssembler Create(int chunkCount, int totalBytes)
    {
        Assert.That(WFMappingDownloadAssembler.TryCreate(1, "Map", "Map.yml", chunkCount, totalBytes, out var assembler),
            Is.True);
        return assembler!;
    }

    [TestCase("My Map.yml", 1, true)]
    [TestCase("My Map_z0.yml", 1, true)]
    [TestCase("My Map_z-2.yml", 3, true)]
    [TestCase("My Map.vessel.yml", 1, true)]
    [TestCase("My Map.yml", 0, false)]
    [TestCase("My Map.yml", WFMappingDownloadAssembler.MaxChunks + 1, false)]
    [TestCase("z0.yml", 1, false)]
    [TestCase("../My Map.yml", 1, false)]
    [TestCase("My Map_z0.grid.yml", 1, false)]
    [TestCase("My Map_z01.yml", 1, false)]
    [TestCase("My Map/../x.yml", 1, false)]
    [TestCase("Other.yml", 1, false)]
    public void TransfersCheckNameAndCount(string name, int chunks, bool valid)
    {
        Assert.That(WFMappingDownloadAssembler.TryCreate(1, "My Map", name, chunks, 100, out _), Is.EqualTo(valid));
    }

    [TestCase(0, false)]
    [TestCase(-1, false)]
    [TestCase(1, true)]
    [TestCase(WFMappingDownloadAssembler.MaxFileBytes, true)]
    [TestCase(WFMappingDownloadAssembler.MaxFileBytes + 1, false)]
    public void TransfersCheckTheAnnouncedSize(int bytes, bool valid)
    {
        Assert.That(WFMappingDownloadAssembler.TryCreate(1, "Map", "Map.yml", 1, bytes, out _), Is.EqualTo(valid));
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
            Assert.That(assembler.Add(index, 3, chunks[index]), Is.EqualTo(WFMappingChunkResult.Accepted));
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
            Assert.That(assembler.Add(0, 2, chunks[0]), Is.EqualTo(WFMappingChunkResult.Accepted));
            Assert.That(assembler.Add(0, 2, chunks[1]), Is.EqualTo(WFMappingChunkResult.Duplicate));
            Assert.That(assembler.Add(2, 2, chunks[1]), Is.EqualTo(WFMappingChunkResult.OutOfRange));
            Assert.That(assembler.Add(-1, 2, chunks[1]), Is.EqualTo(WFMappingChunkResult.OutOfRange));
            Assert.That(assembler.Add(1, 3, chunks[1]), Is.EqualTo(WFMappingChunkResult.OutOfRange));
            Assert.That(assembler.Received, Is.EqualTo(1));
            Assert.That(assembler.IsComplete, Is.False);
        });

        Assert.That(assembler.Add(1, 2, chunks[1]), Is.EqualTo(WFMappingChunkResult.Accepted));
        Assert.That(assembler.TryComplete(out var data), Is.True);
        Assert.That(data, Is.EqualTo(chunks[0].Concat(chunks[1]).ToArray()), "The duplicate didn't replace the first chunk.");
    }

    [Test]
    public void OversizeChunksAreRefused()
    {
        var assembler = Create(2, WFMappingDownloadAssembler.MaxFileBytes);
        Assert.That(assembler.Add(0, 2, new byte[WFMappingDownloadAssembler.ChunkSize + 1]),
            Is.EqualTo(WFMappingChunkResult.TooLarge));
        Assert.That(assembler.Received, Is.Zero);
    }

    [Test]
    public void AFileOfExactlyTheCapCompletes()
    {
        var chunk = new byte[WFMappingDownloadAssembler.ChunkSize];
        chunk[0] = 7;
        var assembler = Create(WFMappingDownloadAssembler.MaxChunks, WFMappingDownloadAssembler.MaxFileBytes);
        for (var i = 0; i < WFMappingDownloadAssembler.MaxChunks; i++)
        {
            Assert.That(assembler.Add(i, WFMappingDownloadAssembler.MaxChunks, chunk), Is.EqualTo(WFMappingChunkResult.Accepted));
        }

        Assert.That(assembler.TryComplete(out var data), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(data, Has.Length.EqualTo(WFMappingDownloadAssembler.MaxFileBytes));
            Assert.That(data[WFMappingDownloadAssembler.ChunkSize], Is.EqualTo(7));
        });
    }

    [Test]
    public void AnEmptyFileIsRefused()
    {
        Assert.That(WFMappingDownloadAssembler.TryCreate(1, "Map", "Map.yml", 1, 0, out _), Is.False);
        Assert.That(WFMappingDownloadAssembler.ChunksFor(0), Is.EqualTo(1));
    }

    [Test]
    public void ChunksMustAddUpToTheAnnouncedSize()
    {
        var chunks = Chunks(2, 4);
        var shortFile = Create(2, 10);
        Assert.That(shortFile.Add(0, 2, chunks[0]), Is.EqualTo(WFMappingChunkResult.Accepted));
        Assert.That(shortFile.Add(1, 2, chunks[1]), Is.EqualTo(WFMappingChunkResult.Accepted));
        Assert.Multiple(() =>
        {
            Assert.That(shortFile.IsComplete, Is.True);
            Assert.That(shortFile.TryComplete(out var data), Is.False, "Eight bytes of an announced ten aren't a file.");
            Assert.That(data, Is.Empty);
        });

        var longFile = Create(2, 6);
        Assert.That(longFile.Add(0, 2, chunks[0]), Is.EqualTo(WFMappingChunkResult.Accepted));
        Assert.That(longFile.Add(1, 2, chunks[1]), Is.EqualTo(WFMappingChunkResult.TooLarge));
    }

    [TestCase(1, 1)]
    [TestCase(WFMappingDownloadAssembler.ChunkSize, 1)]
    [TestCase(WFMappingDownloadAssembler.ChunkSize + 1, 2)]
    public void ChunkCounts(int bytes, int chunks)
    {
        Assert.That(WFMappingDownloadAssembler.ChunksFor(bytes), Is.EqualTo(chunks));
    }
}
