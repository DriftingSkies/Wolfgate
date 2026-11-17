using System.Linq;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ShipPreview;

/// <summary>Every vessel grid ships with the client, which loads it for the ship previewer.</summary>
[TestFixture]
public sealed class VesselGridPathTest
{
    /// <summary>
    /// Client builds leave out /Maps/ (ClientIgnoredResources), so a vessel grid there previews in a dev client and
    /// fails in a packaged one.
    /// </summary>
    [Test]
    public async Task VesselGridsInSharedMapsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        var outside = prototypes.EnumeratePrototypes<VesselPrototype>()
            .Where(vessel => !vessel.ShuttlePath.ToString().StartsWith("/SharedMaps/"))
            .Select(vessel => $"{vessel.ID}: {vessel.ShuttlePath}")
            .ToList();

        Assert.That(outside, Is.Empty, "Vessel grids go under /SharedMaps/, which client builds include.");

        await pair.CleanReturnAsync();
    }
}
