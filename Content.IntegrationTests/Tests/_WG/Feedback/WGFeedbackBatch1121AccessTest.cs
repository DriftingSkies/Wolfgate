#nullable enable
using System.Collections.Generic;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Reports from the 2026-11-21 feedback export: access.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1121AccessTest
{
    /// <summary>#1225: the TSFMC door remote opens the TSFMC door boards Halcyon's doors are built with.</summary>
    [Test]
    public async Task TsfmcDoorRemoteOpensHalcyonDoors()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var reader = entMan.System<AccessReaderSystem>();

        await server.WaitAssertion(() =>
        {
            var remote = entMan.SpawnEntity("DoorRemoteNfsd", MapCoordinates.Nullspace);
            var access = entMan.GetComponent<AccessComponent>(remote);
            var tags = new HashSet<ProtoId<AccessLevelPrototype>>(access.Tags);
            foreach (var group in access.Groups)
                tags.UnionWith(protos.Index(group).Tags);

            var board = entMan.SpawnEntity("DoorElectronicsTsfmc", MapCoordinates.Nullspace);
            var comp = entMan.GetComponent<AccessReaderComponent>(board);
            Assert.That(reader.IsAllowed(tags, new List<StationRecordKey>(), board, comp), Is.True);

            entMan.DeleteEntity(remote);
            entMan.DeleteEntity(board);
        });

        await pair.CleanReturnAsync();
    }
}
