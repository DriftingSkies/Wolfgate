using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.GameRule;
using Content.Server._WG.PlanetCargoHubs;
using Content.Server._WG.TradeGoods.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Presets;
using Content.Server.Station.Systems;
using Content.Shared._NF.CCVar;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.TradeGoods;

[TestFixture]
public sealed class DepotKindsTest
{
    /// <summary>Each kind of depot sells goods the other doesn't, so a round with two depots or more has both.</summary>
    [Test]
    public async Task EveryDepotKindSpawns()
    {
        await RunRounds(planetHub: false);
    }

    /// <summary>A depot on a planet is a standard one, so the space depot left over is always the other kind.</summary>
    [Test]
    public async Task PlanetHubLeavesTheOtherKindInSpace()
    {
        await RunRounds(planetHub: true);
    }

    private static async Task RunRounds(bool planetHub)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            // The space depots only spawn under a preset; a pooled server between rounds may have none.
            var ticker = server.System<GameTicker>();
            var preset = typeof(GameTicker).GetProperty(nameof(GameTicker.CurrentPreset))!;
            var oldPreset = ticker.CurrentPreset;
            var oldCount = server.CfgMan.GetCVar(NFCCVars.CargoDepots);
            var hub = EntityUid.Invalid;
            try
            {
                preset.SetValue(ticker, server.ProtoMan.Index<GamePresetPrototype>("NFAdventure"));
                server.CfgMan.SetCVar(NFCCVars.CargoDepots, 2);

                var kinds = server.ProtoMan.EnumeratePrototypes<PointOfInterestPrototype>().Where(poi => poi.SpawnGroup == "CargoDepot").ToList();
                Assert.That(kinds, Has.Count.EqualTo(2), "Two depots can't hold every kind; raise the count this test spawns.");
                var expected = kinds.Select(kind => kind.ID).ToList();
                if (planetHub)
                {
                    // Depot A stands on a planet, as most rounds have one.
                    hub = entMan.Spawn();
                    entMan.AddComponent<WGPlanetCargoHubComponent>(hub).Index = 0;
                    expected.Remove(WGPlanetCargoHubSystem.DepotKind);
                }

                var maps = server.System<SharedMapSystem>();
                var stations = server.System<StationSystem>();
                for (var round = 0; round < 8; round++)
                {
                    maps.CreateMap(out var mapId);
                    server.System<PointOfInterestSystem>().GenerateDepots(mapId, kinds.ToList(), out var depots);
                    var spawned = depots.Select(depot => entMan.GetComponent<WGTradeStationComponent>(depot).Id).ToList();
                    foreach (var depot in depots)
                    {
                        if (stations.GetOwningStation(depot) is { } station)
                            entMan.DeleteEntity(station);
                    }

                    maps.DeleteMap(mapId);
                    Assert.That(spawned, Is.EquivalentTo(expected), $"Round {round} spawned {string.Join(", ", spawned)}.");
                }
            }
            finally
            {
                if (entMan.EntityExists(hub))
                    entMan.DeleteEntity(hub);
                server.CfgMan.SetCVar(NFCCVars.CargoDepots, oldCount);
                preset.SetValue(ticker, oldPreset);
                server.System<WGPlanetCargoHubSystem>().ResetPlan();
            }
        });

        await pair.CleanReturnAsync();
    }
}
