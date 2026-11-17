#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._NF.SectorServices;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.TradeGoods;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.TradeGoods;

/// <summary>
/// The contraband drop: only one open station buys smuggling crates at a time, it never stays put twice running when
/// there is another, security is tipped off once per drop, and only the Underground sees where it is in the Market app.
/// </summary>
[TestFixture]
[TestOf(typeof(WGTradeContrabandSystem))]
public sealed class TradeContrabandDropTest
{
    private const string Contraband = "WGTradeGoodContraband";

    [Test]
    public async Task OnlyTheDropBuysContraband()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var contraband = entMan.System<WGTradeContrabandSystem>();
        var cartridge = entMan.System<WGMarketCartridgeSystem>();

        await server.WaitAssertion(() =>
        {
            if (!entMan.EntityExists(entMan.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);

            var mallChute = TradeTestWorld.MarketChute(entMan, map);
            var mall = map.Grid.Owner;
            var (depot, depotChute) = TradeTestWorld.Station(entMan, map, new Vector2(0f, 60f), "WG test depot", "CargoChuteDepot");
            var far = TradeTestWorld.Grid(entMan, map, new Vector2(5000f, 0f), "WG test far yard");
            var crate = TradeTestWorld.Hauled(entMan, far, map.GridCoords, Contraband);
            var fabric = TradeTestWorld.Hauled(entMan, far, map.GridCoords);

            var first = contraband.Rotate();
            Assert.That(first, Is.AnyOf(mall, depot), "The drop should go to a station whose chute lists contraband.");
            Assert.That(contraband.DropActive, Is.True);
            Assert.That(contraband.DropLeaked, Is.False, "A new drop starts untold.");

            var (dropChute, otherChute) = first == mall ? (mallChute, depotChute) : (depotChute, mallChute);
            Assert.That(contraband.CheckDrop(dropChute, crate), Is.Null, "The drop refused contraband.");
            Assert.That(contraband.CheckDrop(otherChute, crate), Is.EqualTo((LocId?) "wg-trade-refuse-drop"),
                "A station off the drop took contraband.");
            Assert.That(contraband.CheckDrop(otherChute, fabric), Is.Null, "The drop turned away an ordinary good.");

            // Only the Underground is told where the drop is, by the app's best-sale hint or its alerts.
            EntityUid Holder(string company)
            {
                var holder = entMan.SpawnEntity(null, map.GridCoords);
                entMan.EnsureComponent<CompanyComponent>(holder).CompanyName = company;
                return holder;
            }

            var lawful = Holder("TSFCivilian");
            var underground = Holder("WGUnderground");
            var dropName = TradeTestWorld.Name(entMan, first!.Value);
            Assert.That(cartridge.BestSale(crate, lawful), Is.Null, "A lawful holder was told where the drop is.");
            Assert.That(cartridge.BestSale(crate, underground)?.Station, Is.EqualTo(dropName), "The Underground was not told the drop.");

            WGMarketUiState StateOf(EntityUid holder) => cartridge.BuildState(entMan.SpawnEntity(null, new EntityCoordinates(holder, 0f, 0f)));
            bool SeesDropAlert(WGMarketUiState state) => state.Alerts.Any(alert => alert.Text.Contains(dropName) && alert.Ends == contraband.DropEnds);

            var lawfulState = StateOf(lawful);
            var undergroundState = StateOf(underground);
            Assert.That(SeesDropAlert(lawfulState), Is.False, "A lawful holder saw the drop alert.");
            Assert.That(SeesDropAlert(undergroundState), Is.True, "The Underground's app should show the drop.");

            // Contraband is priced only on the drop's own card under the Underground tab.
            var goodName = entMan.System<WGTradeMarketSystem>().GoodName(Contraband);
            bool Lists(WGMarketStation station) => station.Prices.Any(price => price.Good == goodName);
            Assert.That(lawfulState.Stations.Any(Lists), Is.False, "A lawful holder saw a contraband price.");
            Assert.That(undergroundState.Stations.Any(station => !station.Underground && Lists(station)), Is.False,
                "An open station card listed contraband while there is a drop.");
            Assert.That(undergroundState.Stations.Any(station => station.Underground && station.Name == dropName && Lists(station)), Is.True,
                "The Underground tab should list the drop with its contraband price.");

            contraband.Leak();
            Assert.That(contraband.DropLeaked, Is.True, "Leaking the drop didn't tip off security.");

            var second = contraband.Rotate();
            Assert.That(second, Is.AnyOf(mall, depot));
            Assert.That(second, Is.Not.EqualTo(first), "The drop stayed put though another station could take it.");
            Assert.That(contraband.DropLeaked, Is.False, "A moved drop should be untold again.");
            Assert.That(contraband.CheckDrop(otherChute, crate), Is.Null, "The new drop refused contraband.");
            Assert.That(contraband.CheckDrop(dropChute, crate), Is.Not.Null, "The old drop still took contraband.");

            // With its station gone the drop moves on its next check.
            if (contraband.DropPoint != depot)
                contraband.Rotate();
            Assert.That(contraband.DropPoint, Is.EqualTo(depot));
            entMan.DeleteEntity(depot);
            Assert.That(contraband.DropActive, Is.False, "A drop with no station stayed active.");
            contraband.TickDrop();
            Assert.That(contraband.DropPoint, Is.EqualTo(mall), "The drop should move to the station left standing.");
        });

        await pair.CleanReturnAsync();
    }
}
