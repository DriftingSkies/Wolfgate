#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.ReaverCampaign.Systems;
using Content.Server._WG.SectorControl.Systems;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.SectorControl;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ReaverCampaign;

/// <summary>The end of the campaign: breaking the last stronghold is announced once, and freed Reaver cells are announced as clearing.</summary>
[TestOf(typeof(WGReaverCampaignSystem))]
public sealed class WGReaverEndingTest : WGReaverTestBase
{
    private const string Ending = "The Ashfall Reavers are broken. Their strongholds are gone and the sector is clear.";

    private static readonly WGSectorCell Other = new(-5, 3);

    /// <summary>Breaking the last stronghold announces the ending once and one clearing line naming every freed callsign; the legend and the round summary say so.</summary>
    [Test]
    public async Task BreakingTheLastStrongholdAnnouncesTheEnding()
    {
        await StartCampaign();
        EntityUid stronghold = default;
        var since = 0;
        await Server.WaitAssertion(() =>
        {
            stronghold = FoundAt(Home);
            Assert.That(Server.System<WGReaverCampaignSystem>().State()!.Defeated, Is.False);
        });
        await RunTicks(60);
        since = ChatCount();
        await Server.WaitAssertion(() => KillCrew(stronghold, "lead"));
        await WaitUntilServer(() => SEntMan.GetComponent<WGEncounterComponent>(stronghold).Resolution == WGEncounterResolution.Destroyed, 300);
        // Before the regroup clock (three seconds) founds another.
        await RunTicks(20);
        await Server.WaitAssertion(() =>
        {
            var state = Server.System<WGReaverCampaignSystem>().State()!;
            Assert.That(state.Defeated, Is.True);
            Assert.That(state.Defeats, Is.EqualTo(1));
            Assert.That(Server.System<WGReaverCampaignSystem>().Strongholds(), Is.Empty);
        });

        var lines = ChatSince(since);
        Assert.That(lines.Count(line => line.Contains(Ending)), Is.EqualTo(1), "The ending is announced once.");
        Assert.That(lines.Count(line => line.Contains("is broken.")), Is.EqualTo(1), "The stronghold's own line stays separate.");
        var clearing = lines.Where(line => line.Contains("is clearing.")).ToList();
        Assert.That(clearing, Has.Count.EqualTo(1), "One line for all the cells freed together.");
        Assert.That(clearing[0], Does.Contain("Drone activity returns in 10 minutes. Clear the area."));
        var cells = WGSectorHexCells(Home);
        foreach (var cell in cells)
        {
            Assert.That(clearing[0], Does.Contain(WGSectorHex.Callsign(cell)));
        }

        var listed = string.Join(", ", cells.Select(WGSectorHex.Callsign).OrderBy(callsign => callsign, StringComparer.Ordinal));
        Assert.That(clearing[0], Does.Contain($"Reaver space at {listed} is clearing."));
    }

    /// <summary>Breaking one of two strongholds is no ending.</summary>
    [Test]
    public async Task BreakingOneOfTwoIsNoEnding()
    {
        await StartCampaign();
        EntityUid first = default;
        var since = 0;
        await Server.WaitAssertion(() =>
        {
            first = FoundAt(Home);
            FoundAt(Other);
        });
        await RunTicks(60);
        since = ChatCount();
        await Server.WaitAssertion(() => KillCrew(first, "lead"));
        await WaitUntilServer(() => SEntMan.GetComponent<WGEncounterComponent>(first).Resolution == WGEncounterResolution.Destroyed, 300);
        await RunTicks(90);
        await Server.WaitAssertion(() =>
        {
            var state = Server.System<WGReaverCampaignSystem>().State()!;
            Assert.That(state.Defeated, Is.False);
            Assert.That(state.Defeats, Is.Zero);
        });

        var lines = ChatSince(since);
        Assert.That(lines.Any(line => line.Contains(Ending)), Is.False);
        Assert.That(lines.Count(line => line.Contains("is clearing.")), Is.EqualTo(1), "Its cells are still clearing.");
    }

    private static System.Collections.Generic.List<WGSectorCell> WGSectorHexCells(WGSectorCell cell)
    {
        var cells = new System.Collections.Generic.List<WGSectorCell> { cell };
        cells.AddRange(WGSectorHex.Ring(cell, 1));
        return cells;
    }
}
