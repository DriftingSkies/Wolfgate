#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Ships of a profile that forgives its own never count one another as attackers; other profiles run out of patience with strays.</summary>
    [Test]
    public async Task ForgivingProfileNeverTurnsOnItsOwn()
    {
        var node = await CreateDeck(new Vector2(820, 820), 7, true);
        var sentinel = await CreateDeck(new Vector2(880, 820), 7, true);
        var hauler = await CreateDeck(new Vector2(820, 880), 7, true);
        var other = await CreateDeck(new Vector2(880, 880), 7, true);
        await Server.WaitAssertion(() =>
        {
            var setup = Server.System<WGCrewSetupSystem>();
            var alerts = Server.System<WGCrewAlertSystem>();
            setup.MarkProfile(node, "WGCrewProfileAds");
            setup.MarkProfile(sentinel, "WGCrewProfileAds");
            setup.MarkProfile(hauler, "WGCrewProfileHauler");
            setup.MarkProfile(other, "WGCrewProfileHauler");
            ConvoyCrew(node, "WGCrewGunner", "node");
            ConvoyCrew(hauler, "WGCrewGunner", "hauler");

            Assert.That(alerts.OwnKind(node, sentinel), Is.True, "Two ADS ships are one kind.");
            Assert.That(alerts.OwnKind(hauler, other), Is.False, "Haulers forgive nothing by profile.");
            Assert.That(alerts.OwnKind(node, hauler), Is.False);

            for (var i = 0; i < 40; i++)
            {
                Assert.That(alerts.ReportIncomingFire(node, "node", sentinel), Is.False, "ADS fire on an ADS ship is never an attack.");
            }

            Assert.That(alerts.IsAttacker(node, sentinel), Is.False);
            Assert.That(alerts.IsExcused(node, sentinel), Is.True);
        });
    }
}
