using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Content.Client._WF.LobbyShenanigans;
using Content.Shared._WF.LobbyShenanigans;
using NUnit.Framework;

namespace Content.Tests._WF.LobbyShenanigans;

/// <summary>Every shenanigan the panel can list has a client class to play it and Fluent strings to name it.</summary>
[TestFixture]
public sealed class LobbyShenaniganIdsTest
{
    [Test]
    public void CatalogueMatchesTheClientsClasses()
    {
        // The getter returns a constant, so the classes need not be built (they load sprites).
        var ids = typeof(LobbyShenanigan).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(LobbyShenanigan)))
            .Select(t => ((LobbyShenanigan) RuntimeHelpers.GetUninitializedObject(t)).Id)
            .OrderBy(id => id)
            .ToArray();
        Assert.That(ids, Is.EqualTo(LobbyShenaniganIds.All.OrderBy(id => id).ToArray()));
    }

    [Test]
    public void EachHasANameAndADescription()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Resources")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null);
        var ftl = File.ReadAllText(Path.Combine(dir!, "Resources/Locale/en-US/_WF/LobbyShenanigans/lobby-shenanigans.ftl"));
        foreach (var id in LobbyShenaniganIds.All)
        {
            Assert.That(ftl, Does.Contain($"{LobbyShenaniganIds.Name(id)} ="));
            Assert.That(ftl, Does.Contain($"{LobbyShenaniganIds.Name(id)}-tooltip ="));
        }
    }
}
