using System.Collections.Generic;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Station;

/// <summary>Round-start overflow only hands out Vagrant, never another unlimited job.</summary>
[TestFixture]
[TestOf(typeof(StationJobsSystem))]
public sealed class WGOverflowJobTest
{
    private const string WithVagrantMap = "WGTestOverflowWithVagrant";
    private const string WithoutVagrantMap = "WGTestOverflowWithoutVagrant";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: playTimeTracker
  id: WGTestOverflowWantedTracker

- type: playTimeTracker
  id: WGTestOverflowMarineTracker

- type: playTimeTracker
  id: WGTestOverflowCaptainTracker

- type: job
  id: WGTestOverflowWanted
  playTimeTracker: WGTestOverflowWantedTracker

- type: job
  id: WGTestOverflowMarine
  playTimeTracker: WGTestOverflowMarineTracker

- type: job
  id: WGTestOverflowCaptain
  playTimeTracker: WGTestOverflowCaptainTracker

- type: gameMap
  id: {WithVagrantMap}
  minPlayers: 0
  mapName: {WithVagrantMap}
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: {WithVagrantMap}
      stationProto: StandardNanotrasenStation
      components:
        - type: StationJobs
          availableJobs:
            Contractor: [-1, -1]
            WGTestOverflowMarine: [-1, -1]
            WGTestOverflowCaptain: [1, 1]

- type: gameMap
  id: {WithoutVagrantMap}
  minPlayers: 0
  mapName: {WithoutVagrantMap}
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: {WithoutVagrantMap}
      stationProto: StandardNanotrasenStation
      components:
        - type: StationJobs
          availableJobs:
            WGTestOverflowMarine: [-1, -1]
            WGTestOverflowCaptain: [1, 1]
";

    [Test]
    public async Task OverflowIsVagrantOnlyTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var stationJobs = entMan.System<StationJobsSystem>();
        var stationSystem = entMan.System<StationSystem>();

        var withVagrant = EntityUid.Invalid;
        var withoutVagrant = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            withVagrant = stationSystem.InitializeNewStation(
                protoMan.Index<GameMapPrototype>(WithVagrantMap).Stations["Station"], null, WithVagrantMap);
            withoutVagrant = stationSystem.InitializeNewStation(
                protoMan.Index<GameMapPrototype>(WithoutVagrantMap).Stations["Station"], null, WithoutVagrantMap);
        });

        await server.WaitAssertion(() =>
        {
            var player = pair.Player!.UserId;
            // Only a job no station offers, as when Emergency Responder is set on a Hyperwar round.
            var profile = new HumanoidCharacterProfile()
                .WithJobPriority(SharedGameTicker.FallbackOverflowJob, JobPriority.Never)
                .WithJobPriority("WGTestOverflowWanted", JobPriority.High)
                .WithPreferenceUnavailable(PreferenceUnavailableMode.SpawnAsOverflow);
            var profiles = new Dictionary<NetUserId, HumanoidCharacterProfile> { [player] = profile };

            Assert.That(stationJobs.GetOverflowJobs(withVagrant),
                Is.EquivalentTo(new[] { new ProtoId<JobPrototype>(SharedGameTicker.FallbackOverflowJob) }));
            Assert.That(stationJobs.GetOverflowJobs(withoutVagrant), Is.Empty);

            // No Vagrant slot anywhere: the player stays in the lobby instead of getting a random job.
            var assigned = stationJobs.AssignJobs(new Dictionary<NetUserId, HumanoidCharacterProfile>(profiles), new[] { withoutVagrant });
            stationJobs.AssignOverflowJobs(ref assigned, new[] { player }, profiles, new[] { withoutVagrant });
            Assert.That(assigned, Does.Not.ContainKey(player));
            Assert.That(stationJobs.PickBestAvailableJobWithPriority(withoutVagrant, profile.JobPriorities, true, new HashSet<ProtoId<JobPrototype>>()),
                Is.Null);

            // A Vagrant slot exists: that is the only overflow they can get.
            for (var i = 0; i < 20; i++)
            {
                assigned = stationJobs.AssignJobs(new Dictionary<NetUserId, HumanoidCharacterProfile>(profiles), new[] { withVagrant, withoutVagrant });
                stationJobs.AssignOverflowJobs(ref assigned, new[] { player }, profiles, new[] { withVagrant, withoutVagrant });
                Assert.That(assigned[player], Is.EqualTo(((ProtoId<JobPrototype>?) SharedGameTicker.FallbackOverflowJob, withVagrant)));
            }
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(withVagrant);
            entMan.DeleteEntity(withoutVagrant);
        });

        await pair.CleanReturnAsync();
    }
}
