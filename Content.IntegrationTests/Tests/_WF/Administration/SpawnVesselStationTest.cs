#nullable enable
using System.Numerics;
using Content.Server._WF.Administration.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Administration;

/// <summary>An admin-spawned vessel gets the station a bought one gets, so its expedition console has an owner.</summary>
[TestFixture]
[TestOf(typeof(AdminVesselSpawnSystem))]
public sealed class SpawnVesselStationTest
{
    private const string Vessel = "Judiciary";

    [Test]
    public async Task SpawnedVesselHasItsStation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var maps = server.System<SharedMapSystem>();
        var stations = server.System<StationSystem>();
        var spawner = server.System<AdminVesselSpawnSystem>();

        var map = EntityUid.Invalid;
        EntityUid? grid = null;

        await server.WaitPost(() =>
        {
            map = maps.CreateMap(out var mapId);
            Assert.That(spawner.TrySpawnVessel(proto.Index<VesselPrototype>(Vessel), mapId, Vector2.Zero, null, out grid), Is.True, "The vessel failed to spawn.");
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var station = stations.GetOwningStation(grid!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(station, Is.Not.Null, "The spawned vessel has no station.");
                Assert.That(entMan.HasComponent<SalvageExpeditionDataComponent>(station!.Value), Is.True, "The expedition vessel's station has no expedition data.");
            });

            stations.DeleteStation(station!.Value);
            entMan.DeleteEntity(map);
        });

        await pair.CleanReturnAsync();
    }
}
