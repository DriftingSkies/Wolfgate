#nullable enable
using Content.Server.Administration.Managers;
using Content.Shared.Administration;

namespace Content.IntegrationTests.Tests._WG.Administration;

/// <summary>The command list sent to a client has to name the Toolshed commands its flags allow, or the client refuses them.</summary>
[TestFixture]
[TestOf(typeof(AdminManager))]
public sealed class WGToolshedCommandListTest
{
    [Test]
    public async Task CommandsFollowFlags()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var admins = (AdminManager) server.ResolveDependency<IAdminManager>();

        await server.WaitAssertion(() =>
        {
            var query = admins.WGToolshedCommandsFor(new AdminData { Active = true, Flags = AdminFlags.Query });
            var inactive = admins.WGToolshedCommandsFor(new AdminData { Active = false, Flags = AdminFlags.Query });
            var player = admins.WGToolshedCommandsFor(null);

            Assert.Multiple(() =>
            {
                Assert.That(query, Does.Contain("entities"));
                Assert.That(query, Does.Contain("nearby"));
                Assert.That(query, Does.Not.Contain("delete"), "delete needs DEBUG, not QUERY.");
                Assert.That(inactive, Does.Not.Contain("entities"), "a de-adminned admin got admin commands.");
                Assert.That(player, Does.Not.Contain("entities"), "a player got admin commands.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
