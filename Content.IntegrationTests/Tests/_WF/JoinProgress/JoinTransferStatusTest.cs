#nullable enable
using Content.Shared._WF.JoinProgress;
using ClientJoinProgress = Content.Client._WF.JoinProgress.JoinProgressManager;

namespace Content.IntegrationTests.Tests._WF.JoinProgress;

/// <summary>
/// A connecting client learns which channel its file transfers use. The test network has no websocket, so it is the
/// game connection, which the join console leaves without a fast-channel row.
/// </summary>
[TestFixture]
public sealed class JoinTransferStatusTest
{
    [Test]
    public async Task ClientLearnsItsTransferChannel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true });
        await pair.RunTicksSync(5);

        var joinProgress = pair.Client.ResolveDependency<ClientJoinProgress>();
        Assert.That(joinProgress.TransferMode, Is.EqualTo(JoinTransferMode.GameConnection));

        await pair.CleanReturnAsync();
    }
}
