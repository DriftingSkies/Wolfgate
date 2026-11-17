#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._WG.TradeGoods.Commands;
using Content.Server._WG.TradeGoods.Components;
using Content.Server._WG.TradeGoods.Systems;
using Robust.Server.Console;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.TradeGoods;

/// <summary>
/// The wg_trade admin command starts each trade event at the test's stations, and clear ends the shortages and rush
/// orders it started.
/// </summary>
[TestFixture]
[TestOf(typeof(WGTradeCommand))]
public sealed class TradeCommandTest
{
    [Test]
    public async Task StartsAndClearsEvents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var console = server.ResolveDependency<IServerConsoleHost>();
        var shortages = entMan.System<WGTradeShortageSystem>();
        var rushes = entMan.System<WGTradeRushSystem>();

        await server.WaitAssertion(() =>
        {
            TradeTestWorld.MarketChute(entMan, map);
            var other = TradeTestWorld.Station(entMan, map, new Vector2(5000f, 0f), "WG command test depot");
            var stations = new[] { map.Grid.Owner, other.Grid };

            // Forced to a station-local shortage: a sector-wide boom would outlive the test's grids.
            console.ExecuteCommand(null, "wg_trade shortage WGTradeShortageIndustrial");
            Assert.That(shortages.Active.Count(shortage => shortage.Grid is { } grid && stations.Contains(grid)), Is.EqualTo(1),
                "wg_trade shortage started no shortage at the test's stations.");

            console.ExecuteCommand(null, "wg_trade rush WGTradeRushIndustrial");
            Assert.That(rushes.Active.Count(rush => stations.Contains(rush.Grid)), Is.EqualTo(1),
                "wg_trade rush posted no rush order at the test's stations.");

            console.ExecuteCommand(null, "wg_trade consignment");
            var consignments = entMan.EntityQuery<WGConsignmentComponent>().ToList();
            Assert.That(consignments, Has.Count.EqualTo(1), "wg_trade consignment posted no consignment.");
            Assert.That(stations, Does.Contain(consignments[0].Destination));

            console.ExecuteCommand(null, "wg_trade status");
            console.ExecuteCommand(null, "wg_trade clear");
            Assert.That(shortages.Active, Is.Empty, "wg_trade clear left a shortage running.");
            Assert.That(rushes.Active, Is.Empty, "wg_trade clear left a rush order open.");
        });

        await pair.CleanReturnAsync();
    }
}
