#nullable enable
using System.Numerics;
using System.Reflection;
using Content.Server._WF.Contracts;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Contracts;

/// <summary>
/// Bounties, pickets and wrecks never spawn beside a reality anchor: with the Lattice's six anchors on a 3 km hexagon
/// round the board's station, every site lands at least <see cref="WFContractSystem.AnchorClearance"/> from each,
/// pushed out past the ring rather than dropped between them.
/// </summary>
[TestFixture]
[TestOf(typeof(WFContractSystem))]
public sealed class ContractAnchorClearanceTest
{
    [Test]
    public async Task SitesKeepClearOfAnchors()
    {
        // Dirty: the test builds a station and anchors the next test should not inherit.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var contracts = entMan.System<WFContractSystem>();
        var pickSite = typeof(WFContractSystem).GetMethod("PickSite", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await server.WaitAssertion(() =>
        {
            var station = ContractFlowTest.Station(server, map, Vector2.Zero, "Frontier");
            var anchors = new Vector2[6];
            for (var i = 0; i < anchors.Length; i++)
            {
                anchors[i] = new Angle(System.Math.PI / 3 * i).ToVec() * 3000f;
                entMan.SpawnEntity("WFLatticeAnchorMarker", new MapCoordinates(anchors[i], map.MapId));
            }

            for (var i = 0; i < 50; i++)
            {
                var site = (MapCoordinates?) pickSite.Invoke(contracts, new object?[] { station, 2500f, 3500f, 0f, false });
                Assert.That(site, Is.Not.Null, "No site was found outside the anchor ring.");
                foreach (var anchor in anchors)
                {
                    Assert.That(Vector2.Distance(site!.Value.Position, anchor), Is.GreaterThanOrEqualTo(WFContractSystem.AnchorClearance),
                        $"A site at {site.Value.Position} is beside the anchor at {anchor}.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
