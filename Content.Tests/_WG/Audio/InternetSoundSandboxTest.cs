using System;
using System.IO;
using System.Linq;
using Content.Client._WG.Audio.InternetSound;
using Content.Shared._WG.Audio.InternetSound;
using Moq;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.Log;

namespace Content.Tests._WG.Audio;

[TestFixture]
public sealed class InternetSoundSandboxTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void ContentAssembliesPassClientSandbox(bool client)
    {
        // Unit/integration game instances normally skip the real client's sandbox checks.
        // Invoke the engine checker from the test assembly without modifying engine policy.
        var assembly = client ? typeof(InternetSoundSystem).Assembly : typeof(InternetSoundResources).Assembly;
        var checkerType = typeof(IResourceManager).Assembly.GetType("Robust.Shared.ContentPack.AssemblyTypeChecker", true)!;
        var log = new Mock<ISawmill>();
        var checker = Activator.CreateInstance(checkerType, new Mock<IResourceManager>().Object, log.Object)!;
        checkerType.GetField("EngineModuleDirectories")!.SetValue(checker, new[] { Path.GetDirectoryName(assembly.Location) });
        using var stream = File.OpenRead(assembly.Location);
        var passed = (bool) checkerType.GetMethod("CheckAssembly", new[] { typeof(Stream) })!.Invoke(checker, new object[] { stream })!;
        var messages = string.Join(Environment.NewLine, log.Invocations.Select(x => string.Join(" ", x.Arguments)));
        Assert.That(passed, Is.True, messages);
    }
}
