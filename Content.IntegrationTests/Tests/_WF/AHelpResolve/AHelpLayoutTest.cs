#nullable enable
using Content.Client.Administration.UI.Bwoink;

namespace Content.IntegrationTests.Tests._WF.AHelpResolve;

/// <summary>The ahelp window opens with room for its player list, beside the chat and its buttons.</summary>
[TestFixture]
public sealed class AHelpLayoutTest
{
    /// <summary>The share of the window's width the player list gets at least.</summary>
    private const float MinListShare = 0.25f;

    [Test]
    public async Task PlayerListIsNotSquished()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        BwoinkWindow? window = null;
        await client.WaitPost(() =>
        {
            window = new BwoinkWindow();
            window.OpenCentered();
        });
        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var list = window!.Bwoink.ChannelSelector;
            Assert.That(list.Width, Is.GreaterThanOrEqualTo(window.Width * MinListShare),
                $"the player list is {list.Width} wide in a {window.Width} window");
            window.Close();
        });

        await pair.CleanReturnAsync();
    }
}
