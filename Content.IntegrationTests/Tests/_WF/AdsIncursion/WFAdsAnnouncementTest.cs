#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.AdsIncursion.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.AdsIncursion;

/// <summary>The sector watch banners only capital moments; the rest is a radio line, and bookkeeping says nothing.</summary>
[TestOf(typeof(WFAdsCampaignSystem))]
public sealed class WFAdsAnnouncementTest : WFAdsTestBase
{
    private const string Founded = "An Automated Defense System node has come online at";
    private const string Joined = "An ADS sentinel has joined the node at";
    private const string DeploymentLaunched = "An ADS deployment is under way to";
    private const string Falling = "Automated Defense System threat is falling";

    /// <summary>Two cells beyond the node's ring.</summary>
    private (WFSectorCell, WFSectorCell) Targets()
    {
        var cells = WFSectorHex.Ring(AdsHome, 3).OrderByDescending(cell => WFSectorHex.Distance(cell, WFSectorCell.Origin)).ToList();
        return (cells[0], cells[1]);
    }

    /// <summary>The first founding chimes once and is a banner, with no radio line.</summary>
    [Test]
    public async Task FirstFoundingIsOneChimedBanner()
    {
        await StartAds();
        // A tier change in the same tick would take the chime; this one is past.
        await Server.WaitAssertion(() => Server.System<WFAdsCampaignSystem>().SetTier(5));
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var chimes = Chimes();
            FoundNode(AdsHome);
            Assert.That(Chimes() - chimes, Is.EqualTo(1), "One chime.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, Founded), Is.EqualTo(1));
        Assert.That(RadioLines(since, Founded), Is.Zero);
    }

    /// <summary>A later founding is a radio line: no banner and no chime.</summary>
    [Test]
    public async Task LaterFoundingIsARadioLine()
    {
        await StartAds();
        await Server.WaitAssertion(() =>
        {
            Server.System<WFAdsCampaignSystem>().SetTier(5);
            FoundNode(AdsHome);
        });
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var chimes = Chimes();
            FoundNode(Targets().Item1);
            Assert.That(Chimes(), Is.EqualTo(chimes), "No chime.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, Founded), Is.Zero);
        Assert.That(RadioLines(since, Founded), Is.EqualTo(1));
    }

    /// <summary>A sentinel joining its node says nothing.</summary>
    [Test]
    public async Task SentinelIsSilent()
    {
        await StartAds();
        EntityUid node = default;
        await Server.WaitAssertion(() => node = FoundNode(AdsHome));
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var chimes = Chimes();
            Assert.That(ads.TrySentinel(node), Is.True);
            Assert.That(ads.Join(ads.Sentinels().Single()), Is.True);
            Assert.That(Chimes(), Is.EqualTo(chimes), "No chime.");
        });
        await RunTicks(30);
        Assert.That(ChatSince(since).Any(line => line.Contains(Joined)), Is.False);
    }

    /// <summary>Deployments are radio lines, however many go out.</summary>
    [Test]
    public async Task DeploymentsAreRadioLines()
    {
        await StartAds();
        await Server.WaitAssertion(() => FoundNode(AdsHome));
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var ads = Server.System<WFAdsCampaignSystem>();
            var (first, second) = Targets();
            var chimes = Chimes();
            Assert.That(ads.TryLaunchDeployment(first, out _), Is.True);
            Assert.That(ads.TryLaunchDeployment(second, out _), Is.True);
            Assert.That(Chimes(), Is.EqualTo(chimes), "Neither chimes.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, DeploymentLaunched), Is.Zero);
        Assert.That(RadioLines(since, DeploymentLaunched), Is.EqualTo(2));
    }

    /// <summary>A falling tier is a radio line, not a banner.</summary>
    [Test]
    public async Task FallingTierIsARadioLine()
    {
        await StartAds();
        await Server.WaitAssertion(() => Server.System<WFAdsCampaignSystem>().SetTier(5));
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var chimes = Chimes();
            Server.System<WFAdsCampaignSystem>().SetTier(1);
            Assert.That(Chimes(), Is.EqualTo(chimes), "No chime.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, Falling), Is.Zero);
        Assert.That(RadioLines(since, Falling), Is.EqualTo(1));
    }
}
