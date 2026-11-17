#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Server._Mono.AlertLevel;
using Content.Server._NF.SectorServices;
using Content.Server._WG.Encounters;
using Content.Server._WG.TradeGoods.Components;
using Content.Server._WG.TradeGoods.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.Encounters;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.TradeGoods;

/// <summary>
/// War pricing: while the war is hot a navy pays the enemy's people more for war goods and its own people
/// faction credits on top; freight runs are drawn to stations that are short.
/// </summary>
[TestFixture]
[TestOf(typeof(WGTradeWarSystem))]
public sealed class TradeWarTest
{
    private static readonly ProtoId<WGEncounterPrototype> WGEncounterFreightSmallProto = "WGEncounterFreightSmall";

    private const string TsfChute = "CargoChuteTSFMC";
    private const string Ammo = "TradeGoodAmmo";

    [Test]
    public async Task NaviesPayForTheWar()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();
        var war = entMan.System<WarLevelSystem>();

        await server.WaitAssertion(() =>
        {
            entMan.EnsureComponent<WGTradeMarketMapComponent>(map.MapUid);
            var chute = entMan.SpawnEntity(TsfChute, map.GridCoords);
            var pdv = entMan.SpawnEntity(null, map.GridCoords);
            entMan.AddComponent<CompanyComponent>(pdv).CompanyName = "PDV";

            // The war level lives on the sector service; a test map hosts one if the server has none.
            if (!entMan.EntityExists(entMan.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);
            entMan.EnsureComponent<WarLevelComponent>(entMan.System<SectorServiceSystem>().GetServiceEntity());
            var peace = market.Quote(chute, Ammo, null, pdv)!.Value;
            war.SetLevel(true);
            try
            {
            Assert.That(war.GetWarLevel(chute), Is.True, "The test needs the war level to go hot.");

            var hot = market.Quote(chute, Ammo, null, pdv)!.Value;
            Assert.That(hot.Amount, Is.GreaterThan(peace.Amount), "The TSF should pay a PDV seller more for ammunition in wartime.");

            // The TSF's own people get faction credits on top for a hauled war-goods crate.
            var tsf = entMan.SpawnEntity(null, map.GridCoords);
            entMan.AddComponent<CompanyComponent>(tsf).CompanyName = "TSF";
            var mapSys = entMan.System<SharedMapSystem>();
            var far = mapSys.CreateGridEntity(map.MapId);
            mapSys.SetTile(far.Owner, far.Comp, Vector2i.Zero, map.Tile.Tile);
            entMan.System<SharedTransformSystem>().SetLocalPosition(far.Owner, new Vector2(5000f, 0f));
            var crate = entMan.SpawnEntity(Ammo, new EntityCoordinates(far.Owner, 0.5f, 0.5f));
            entMan.System<SharedTransformSystem>().SetCoordinates(crate, map.GridCoords);
            market.Sell(chute, crate, tsf);

            var credits = 0;
            var query = entMan.AllEntityQueryEnumerator<MetaDataComponent>();
            while (query.MoveNext(out _, out var meta))
            {
                if (meta.EntityPrototype?.ID.StartsWith("FederationMilitaryCredit") == true)
                    credits++;
            }

            Assert.That(credits, Is.GreaterThan(0), "A TSF seller should get military credits for war goods in wartime.");
            }
            finally
            {
                // The pooled server is shared; leave the war cold for the next test.
                war.SetLevel(false);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FreightIsDrawnToShortStations()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var shortages = entMan.System<WGTradeShortageSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var freight = protos.Index<WGEncounterPrototype>(WGEncounterFreightSmallProto);
            var station = map.Grid.Owner;

            var before = new WGEncounterStationWeightEvent(freight, station, 1f);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref before);
            Assert.That(before.Weight, Is.EqualTo(1f));

            shortages.StartLocal(station, new HashSet<string> { Ammo }, 1.5f, System.TimeSpan.FromMinutes(5), "wg-trade-goods-ammo");
            var after = new WGEncounterStationWeightEvent(freight, station, 1f);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref after);
            Assert.That(after.Weight, Is.GreaterThan(1f), "A short station should draw freight.");
        });

        await pair.CleanReturnAsync();
    }
}
