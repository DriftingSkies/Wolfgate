#nullable enable
using System.Threading.Tasks;
using Content.Server._WG.Shipyard;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Traders;

/// <summary>A used ship stays off the lot until its copy is written, and a copy that fails to write is dropped.</summary>
[TestFixture]
[TestOf(typeof(UsedShipMarketSystem))]
public sealed class UsedShipListingReadyTest
{
    [Test]
    public void ReadyFollowsTheWrite()
    {
        var writing = new TaskCompletionSource<string?>();
        var pending = new UsedShipListing { Writing = writing.Task };
        Assert.That(pending.Ready, Is.False, "A listing still being written is not ready.");
        Assert.That(pending.WriteFailed, Is.False);

        writing.SetResult("yaml");
        Assert.That(pending.Ready, Is.True);
        Assert.That(pending.Data, Is.EqualTo("yaml"));

        var failed = new UsedShipListing { Writing = Task.FromResult<string?>(null) };
        Assert.That(failed.Ready, Is.False);
        Assert.That(failed.WriteFailed, Is.True);

        Assert.That(new UsedShipListing().Ready, Is.True, "A listing with nothing being written is ready.");
    }

    /// <summary>The market drops a listing whose copy failed to write, rather than leaving it to sell a ship that can't load.</summary>
    [Test]
    public async Task MarketDropsAFailedWrite()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<UsedShipMarketSystem>();
        var maps = entMan.System<SharedMapSystem>();

        await server.WaitAssertion(() =>
        {
            var grid = maps.CreateGridEntity(map.MapId);
            maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, map.Tile.Tile);
            Assert.That(market.TryCapture(grid.Owner, map.Grid.Owner, 100, 0f, TimeSpan.Zero, out var listing), Is.True);

            listing!.Writing = Task.FromResult<string?>(null);
            Assert.That(market.GetAvailable(), Does.Not.Contain(listing), "A failed copy should not be on the lot.");

            market.Update(0f);
            Assert.That(market.Listings, Does.Not.Contain(listing), "A failed copy should be dropped.");
            entMan.DeleteEntity(grid.Owner);
        });

        await pair.CleanReturnAsync();
    }
}
