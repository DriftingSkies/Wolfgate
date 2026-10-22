#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._NF.Station.Components;
using Content.Server._WF.Administration.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Administration;

/// <summary>
/// An admin-spawned vessel gets its late-join station and job slots without an owner, can be renamed, and keeps
/// that station and name when it is later deeded.
/// </summary>
[TestFixture]
[TestOf(typeof(AdminVesselSpawnSystem))]
public sealed class VesselSpawnStationTest
{
    private const string VesselProto = "Guppy";
    private const string IdProto = "PassengerIDCard";
    private const string NewName = "Test Vessel";

    [Test]
    public async Task SpawnedVesselHasJobsAndRenames()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var stations = entMan.System<StationSystem>();
        var jobs = entMan.System<StationJobsSystem>();
        var shipyard = entMan.System<ShipyardSystem>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid grid = default, station = default;
        await server.WaitAssertion(() =>
        {
            // Deeds write a sector shuttle record; the host component gives it somewhere to go.
            entMan.EnsureComponent<StationSectorServiceHostComponent>(entMan.Spawn());

            var vessel = server.ProtoMan.Index<VesselPrototype>(VesselProto);
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(80, 0), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;

            Assert.That(stations.GetOwningStation(grid), Is.Not.Null, "The vessel should get a station without an owner.");
            station = stations.GetOwningStation(grid)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(jobs.GetJobs(station), Is.Not.Empty, "The station should carry the design's job slots.");
                Assert.That(entMan.GetComponent<ExtraShuttleInformationComponent>(station).Vessel?.Id, Is.EqualTo(VesselProto));
                Assert.That(entMan.GetComponent<MetaDataComponent>(grid).EntityName, Is.EqualTo(entMan.GetComponent<MetaDataComponent>(station).EntityName),
                    "The grid takes the station's generated name.");
            });

            Assert.That(spawner.TryRenameVessel(grid, NewName, null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<MetaDataComponent>(grid).EntityName, Is.EqualTo(NewName));
                Assert.That(entMan.GetComponent<MetaDataComponent>(station).EntityName, Is.EqualTo(NewName));
            });

            var card = entMan.SpawnEntity(IdProto, map.GridCoords);
            Assert.That(shipyard.TryAssignDeed(grid, card, session, vessel), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(stations.GetOwningStation(grid), Is.EqualTo(station), "The deed should reuse the spawn station.");
                Assert.That(entMan.GetComponent<ShuttleDeedComponent>(card).ShuttleName, Is.EqualTo(NewName), "The deed keeps the given name.");
            });

            Assert.That(spawner.TryRenameVessel(grid, "Second Name", null), Is.True);
            Assert.That(entMan.GetComponent<ShuttleDeedComponent>(card).ShuttleName, Is.EqualTo("Second Name"), "Renaming a deeded vessel updates its deeds.");

            entMan.DeleteEntity(card);
            entMan.DeleteEntity(grid);
            entMan.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }
}
