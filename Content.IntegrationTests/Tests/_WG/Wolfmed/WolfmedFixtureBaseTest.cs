#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using NUnit.Framework;

namespace Content.IntegrationTests.Tests._WG.Wolfmed;

/// <summary>
/// Every Wolfmed fixture is a <see cref="WolfmedGameTest"/>, so a new one cannot go back to the pair's bare vacuum
/// map without the base noticing.
/// </summary>
[TestFixture]
public sealed class WolfmedFixtureBaseTest
{
    [Test]
    public void EveryFixtureDerivesFromTheWolfmedBaseTest()
    {
        var wolfmed = typeof(WolfmedGameTest).Namespace!;
        var plain = typeof(WolfmedGameTest).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(GameTest).IsAssignableFrom(type) &&
                           !typeof(WolfmedGameTest).IsAssignableFrom(type) &&
                           (type.Namespace == wolfmed || (type.Namespace?.StartsWith(wolfmed + ".") ?? false)))
            .Select(type => type.FullName)
            .ToList();

        Assert.That(plain, Is.Empty, "Wolfmed fixtures on plain GameTest; derive them from WolfmedGameTest.");
    }
}
