#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WG.SectorControl;
using Content.Server._WG.SectorControl.Systems;
using Content.Shared._WG.SectorControl;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>The Reaver faction replaces the sector control border notice with its own, blunter line.</summary>
[TestOf(typeof(WGSectorNoticeSystem))]
public sealed class WGReaverNoticeTest : WGSectorTestBase
{
    private const string Reavers = "WGSectorFactionReavers";

    private static readonly WGSectorCell Cell = new(4, -2);

    [Test]
    public async Task ReaverSpaceNoticeUsesTheReaverLine()
    {
        await StartRound();
        var since = ChatCount();
        TimeSpan entered = default;
        await Server.WaitAssertion(() =>
        {
            var ship = SpawnGrid(CentreOf(Cell), "Test Hauler");
            Board(ship);
            Assert.That(Server.System<WGSectorTerritorySystem>().TryClaim(MapData.MapId, Cell, Reavers, null), Is.True);
            Server.System<WGSectorNoticeSystem>().Check();
            entered = STiming.CurTime;
        });

        await WaitUntilServer(() => STiming.CurTime - entered > WGSectorNoticeSystem.EnteredDwell + TimeSpan.FromSeconds(0.5), 1200);
        await Server.WaitPost(() => Server.System<WGSectorNoticeSystem>().Check());
        await RunTicks(60);
        var line = $"WARNING: entering Ashfall Reaver controlled space ({WGSectorHex.Callsign(Cell)}). Their patrols attack on sight";
        Assert.That(ChatSince(since).Count(message => message.Contains(line)), Is.EqualTo(1));
        Assert.That(ChatSince(since).Any(message => message.Contains("Ashfall Reavers controlled space")), Is.False,
            "The default line is not sent as well.");
    }
}
