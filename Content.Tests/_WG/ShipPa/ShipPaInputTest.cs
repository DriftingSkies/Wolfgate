using Content.Client._WG.Administration.UI.InternetSound;
using Content.Server._WG.ShipPa;
using NUnit.Framework;

namespace Content.Tests._WG.ShipPa;

[TestFixture]
public sealed class ShipPaInputTest
{
    [TestCase("https://user:password@example.com:8443/audio?token=secret#start", "https://example.com:8443")]
    [TestCase("https://example.com/audio?token=secret#start", "https://example.com")]
    public void SafeOriginRemovesSensitiveUrlParts(string url, string expected)
    {
        Assert.That(ShipAlertSystem.GetSafeOrigin(url), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("1 2")]
    [TestCase("\"1\"")]
    [TestCase("not-a-net-entity")]
    [TestCase("0")]
    public void InvalidGridInputIsRejected(string grid)
    {
        Assert.That(InternetSoundAdminWindow.IsValidGridInput(grid), Is.False);
    }

    [TestCase("1")]
    [TestCase("c42")]
    public void ValidGridInputIsAccepted(string grid)
    {
        Assert.That(InternetSoundAdminWindow.IsValidGridInput(grid), Is.True);
    }
}
