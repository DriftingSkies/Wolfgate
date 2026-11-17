using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.IO;
using Robust.Shared.Utility;
using Content.Shared._WG.Audio.InternetSound;
using Moq;
using NUnit.Framework;
using Robust.Shared.ContentPack;

namespace Content.Tests._WG.Audio;

[TestFixture]
public sealed class InternetSoundResourcesTest
{
    [Test]
    public void RegistryDoesNotKeepDiscardedResourceManagersAlive()
    {
        var manager = new Mock<IResourceManager>().Object;
        var mounted = InternetSoundResources.For(manager);
        var reused = InternetSoundResources.For(manager);
        Assert.That(reused, Is.SameAs(mounted),
            "A live manager must reuse its mounted root across reconnects.");
        var discarded = CreateDiscardedManager();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(discarded.IsAlive, Is.False, "The static registry must not retain discarded test instances.");
        GC.KeepAlive(manager);
    }

    [Test]
    public void MountedRootSurvivesReconnectAndDelegatesFileAccess()
    {
        var mounts = new List<IContentRoot>();
        var manager = new Mock<IResourceManager>();
        manager.Setup(x => x.AddRoot(InternetSoundResources.Prefix, It.IsAny<IContentRoot>()))
            .Callback<ResPath, IContentRoot>((_, root) => mounts.Add(root));
        var rootReference = MountTrack(manager.Object);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var root = InternetSoundResources.For(manager.Object);
        Assert.That(rootReference.TryGetTarget(out var original), Is.True);
        Assert.That(root, Is.SameAs(original));
        Assert.That(mounts, Has.Count.EqualTo(1), "Reconnect must not add another permanent root.");
        var mounted = mounts[0];
        mounted.Mount();
        var path = new ResPath("123.ogg");
        Assert.That(mounted.FileExists(path), Is.True);
        Assert.That(mounted.FindFiles(ResPath.Self), Does.Contain(path));
        Assert.That(mounted.GetRelativeFilePaths(), Does.Contain("123.ogg"));
        Assert.That(mounted.TryGetFile(path, out var stream), Is.True);
        using (stream)
        using (var output = new MemoryStream())
        {
            stream.CopyTo(output);
            Assert.That(output.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
        root.Clear();
        Assert.That(mounted.FileExists(path), Is.False);
        GC.KeepAlive(manager);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<InternetSoundResources> MountTrack(IResourceManager manager)
    {
        var root = InternetSoundResources.For(manager);
        root.Store(123, new byte[] { 1, 2, 3 });
        return new WeakReference<InternetSoundResources>(root);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateDiscardedManager()
    {
        var manager = new Mock<IResourceManager>().Object;
        InternetSoundResources.For(manager);
        return new WeakReference(manager);
    }
}
