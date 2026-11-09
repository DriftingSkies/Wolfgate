#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WF.ReaverCampaign.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.ReaverCampaign;

/// <summary>The sector watch banners only capital moments; the rest is a radio line, and bookkeeping says nothing.</summary>
[TestOf(typeof(WFReaverCampaignSystem))]
public sealed class WFReaverAnnouncementTest : WFReaverTestBase
{
    private const string Founded = "Reavers have built a stronghold at";
    private const string Inbound = "Reaver reinforcements are inbound to";
    private const string ConvoyLaunched = "A Reaver convoy is under way to";
    private const string Falling = "Reaver threat is falling";

    /// <summary>Two cells beyond the home stronghold's ring.</summary>
    private (WFSectorCell, WFSectorCell) Targets()
    {
        var cells = WFSectorHex.Ring(Home, 3).OrderByDescending(cell => WFSectorHex.Distance(cell, WFSectorCell.Origin)).ToList();
        return (cells[0], cells[1]);
    }

    /// <summary>The first founding chimes once and is a banner, with no radio line.</summary>
    [Test]
    public async Task FirstFoundingIsOneChimedBanner()
    {
        await StartCampaign();
        // A tier change in the same tick would take the chime; this one is past.
        await Server.WaitAssertion(() => Server.System<WFReaverCampaignSystem>().SetTier(5));
        await RunTicks(5);

        var since = ChatCount();
        var chimes = 0;
        await Server.WaitAssertion(() =>
        {
            chimes = Chimes();
            FoundAt(Home);
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
        await StartCampaign();
        await Server.WaitAssertion(() =>
        {
            Server.System<WFReaverCampaignSystem>().SetTier(5);
            FoundAt(Home);
        });
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var chimes = Chimes();
            FoundAt(new WFSectorCell(-5, 3));
            Assert.That(Chimes(), Is.EqualTo(chimes), "No chime.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, Founded), Is.Zero);
        Assert.That(RadioLines(since, Founded), Is.EqualTo(1));
    }

    /// <summary>A reinforcement launch says nothing.</summary>
    [Test]
    public async Task ReinforcementLaunchIsSilent()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        await Server.WaitAssertion(() => stronghold = FoundAt(Home));
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var chimes = Chimes();
            Assert.That(Server.System<WFReaverCampaignSystem>().TryReinforce(stronghold), Is.True);
            Assert.That(Chimes(), Is.EqualTo(chimes), "No chime.");
        });
        await RunTicks(30);
        Assert.That(ChatSince(since).Any(line => line.Contains(Inbound)), Is.False);
    }

    /// <summary>Convoys are radio lines, however many go out.</summary>
    [Test]
    public async Task ConvoysAreRadioLines()
    {
        await StartCampaign();
        await Server.WaitAssertion(() => FoundAt(Home));
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var campaign = Server.System<WFReaverCampaignSystem>();
            var (first, second) = Targets();
            var chimes = Chimes();
            Assert.That(campaign.TryLaunchConvoy(first, out _), Is.True);
            Assert.That(campaign.TryLaunchConvoy(second, out _), Is.True);
            Assert.That(Chimes(), Is.EqualTo(chimes), "Neither chimes.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, ConvoyLaunched), Is.Zero);
        Assert.That(RadioLines(since, ConvoyLaunched), Is.EqualTo(2));
    }

    /// <summary>A falling tier is a radio line, not a banner.</summary>
    [Test]
    public async Task FallingTierIsARadioLine()
    {
        await StartCampaign();
        await Server.WaitAssertion(() => Server.System<WFReaverCampaignSystem>().SetTier(5));
        await RunTicks(5);

        var since = ChatCount();
        await Server.WaitAssertion(() =>
        {
            var chimes = Chimes();
            Server.System<WFReaverCampaignSystem>().SetTier(1);
            Assert.That(Chimes(), Is.EqualTo(chimes), "No chime.");
        });
        await RunTicks(30);
        Assert.That(Banners(since, Falling), Is.Zero);
        Assert.That(RadioLines(since, Falling), Is.EqualTo(1));
    }
}
