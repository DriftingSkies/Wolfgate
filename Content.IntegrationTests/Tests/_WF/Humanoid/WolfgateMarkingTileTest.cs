using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Humanoid;

/// <summary>
/// A marking tile is as wide as the picker's grid assumes whatever the size of the marking's sprites, so a body part
/// with oversized sprites, such as the 64 px naga tails, does not push the grid past the picker's edge.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfgateMarkingTile))]
public sealed class WolfgateMarkingTileTest
{
    [Test]
    public async Task TilesFitTheGridTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var wide = new List<string>();
        var count = 0;

        await client.WaitPost(() =>
        {
            foreach (var marking in client.ResolveDependency<MarkingManager>().Markings.Values)
            {
                var tile = new WolfgateMarkingTile(marking.ID, marking.ID, marking.Sprites, Direction.South);
                tile.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
                count++;
                if (tile.DesiredSize.X > WolfgateMarkingTile.TileWidth)
                    wide.Add($"{marking.ID} ({tile.DesiredSize.X})");
            }
        });

        Assert.That(count, Is.GreaterThan(0));
        Assert.That(wide, Is.Empty, "tiles wider than the grid's columns");
        await pair.CleanReturnAsync();
    }
}
