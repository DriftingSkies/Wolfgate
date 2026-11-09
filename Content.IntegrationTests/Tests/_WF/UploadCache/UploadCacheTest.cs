#nullable enable
using System.Collections.Generic;
using System.IO;
using Content.Client._WF.UploadCache;
using ServerUploadCache = Content.Server._WF.UploadCache.WFUploadCacheSystem;
using Content.Shared._WF.UploadCache;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.UploadCache;

/// <summary>
/// A shared file reaches a connected client, is kept on its disk, and a later list naming the same contents is served
/// from that disk instead of being fetched again.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerUploadCache))]
public sealed class UploadCacheTest
{
    [Test]
    public async Task SharedFileIsFetchedOnceAndCached()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true });
        var server = pair.Server;
        var client = pair.Client;
        var cache = server.System<ServerUploadCache>();
        var clientCache = client.ResolveDependency<WFUploadCacheManager>();
        var clientResources = client.ResolveDependency<IResourceManager>();

        var data = new byte[200_000];
        new Random(4).NextBytes(data);
        var hash = WFUploadCache.Hash(data);
        var relative = new ResPath("Test/first.bin");

        await server.WaitPost(() => Assert.That(cache.Put(relative, data), Is.EqualTo(1), "the connected client was not told"));
        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            var path = WFUploadCache.Root / relative;
            Assert.That(clientResources.ContentFileExists(path), Is.True, "the client never mounted the shared file");
            using var stream = clientResources.ContentFileRead(path);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Assert.That(copy.ToArray(), Is.EqualTo(data));
            Assert.That(clientResources.UserData.Exists(new ResPath("/wolfgate/upload_cache") / hash), Is.True,
                "the client did not keep the file on disk");

            // The same contents under another name, as after a restart: served from disk, nothing asked for.
            var manifest = new WFUploadManifestEvent();
            manifest.Files.Add(new WFUploadEntry { Path = "Test/again.bin", Hash = hash, Size = data.Length });
            Assert.That(clientCache.Apply(manifest), Is.Empty, "a cached file was asked for again");
            Assert.That(clientResources.ContentFileExists(WFUploadCache.Root / "Test/again.bin"), Is.True);
            Assert.That(clientResources.ContentFileExists(WFUploadCache.Root / relative), Is.False,
                "a file the list no longer names stayed mounted");
        });

        // The server stops sharing it: the client drops it from memory and disk.
        await server.WaitPost(() => cache.Remove(relative));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(clientResources.ContentFileExists(WFUploadCache.Root / "Test/again.bin"), Is.False);
            Assert.That(clientResources.UserData.Exists(new ResPath("/wolfgate/upload_cache") / hash), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The transfer stream's file format reads back what was written, files in order.</summary>
    [Test]
    public async Task TransferStreamRoundTrips()
    {
        var first = new byte[70_000];
        var second = new byte[] { 1, 2, 3 };
        new Random(9).NextBytes(first);
        var files = new List<(string, byte[])> { (WFUploadCache.Hash(first), first), (WFUploadCache.Hash(second), second) };

        using var stream = new MemoryStream();
        await WFUploadCache.WriteFiles(stream, files);
        stream.Position = 0;

        var read = new List<(string, byte[])>();
        await WFUploadCache.ReadFiles(stream, (hash, data) => read.Add((hash, data)));
        Assert.That(read, Is.EqualTo(files));
    }
}
