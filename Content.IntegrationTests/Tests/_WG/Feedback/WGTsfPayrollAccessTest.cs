#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>A TSFMC Fireteam Leader's access opens the TSFMC admin consoles, to pay the marines from the FOB's account.</summary>
[TestFixture]
public sealed class WGTsfPayrollAccessTest
{
    [Test]
    public async Task FireteamLeaderOpensThePayrollConsoles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var reader = entMan.System<AccessReaderSystem>();

        await server.WaitAssertion(() =>
        {
            var job = protos.Index<JobPrototype>("SeniorOfficer");
            var access = new HashSet<ProtoId<AccessLevelPrototype>>(job.Access);
            foreach (var group in job.AccessGroups)
                access.UnionWith(protos.Index(group).Tags);
            var deputy = protos.Index<JobPrototype>("Deputy");
            var marine = new HashSet<ProtoId<AccessLevelPrototype>>(deputy.Access);
            foreach (var group in deputy.AccessGroups)
                marine.UnionWith(protos.Index(group).Tags);

            foreach (var id in new[] { "StationAdminBankATMNfsd", "ComputerTabletopStationAdminBankATMNfsd" })
            {
                var console = entMan.SpawnEntity(id, MapCoordinates.Nullspace);
                var comp = entMan.GetComponent<AccessReaderComponent>(console);
                Assert.That(reader.IsAllowed(access, new List<StationRecordKey>(), console, comp), Is.True, $"A Fireteam Leader can't open the {id}.");
                Assert.That(reader.IsAllowed(marine, new List<StationRecordKey>(), console, comp), Is.False, $"A plain marine can open the {id}.");
                entMan.DeleteEntity(console);
            }
        });

        await pair.CleanReturnAsync();
    }
}
