#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WG.SectorControl;
using Content.Server._WG.SectorControl;
using Content.Server._WG.SectorControl.Systems;
using Content.Shared._WG.SectorControl;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.SectorControl;

/// <summary>Clearing: a freed cell is kept clear of drones for a grace period, shown on the map, and left when claimed again.</summary>
[TestOf(typeof(WGSectorTerritorySystem))]
public sealed class WGSectorClearingTest : WGSectorTestBase
{
    private static readonly WGSectorCell Far = new(4, -2);
    private static readonly WGSectorCell Next = new(5, -2);

    private IConfigurationManager ServerConfig => Server.ResolveDependency<IConfigurationManager>();

    [TearDown]
    public async Task ResetClearing()
    {
        await Server.WaitPost(() =>
        {
            ServerConfig.SetCVar(SectorControlCVars.ClearingMinutes, SectorControlCVars.ClearingMinutes.DefaultValue);
            Server.System<WGSectorTerritorySystem>().Clear(MapData.MapId);
            Server.System<WGSectorTestSystem>().Reset();
        });
    }

    /// <summary>Freeing a held cell starts clearing for the configured time, keeps it so until then, and raises both events.</summary>
    [Test]
    public async Task FreedCellClearsForTheConfiguredTime()
    {
        await StartRound();
        TimeSpan until = default;
        await Server.WaitAssertion(() =>
        {
            ServerConfig.SetCVar(SectorControlCVars.ClearingMinutes, 0.05f);
            var territory = Server.System<WGSectorTerritorySystem>();
            var recorder = Server.System<WGSectorTestSystem>();
            var map = MapData.MapId;
            recorder.Reset();
            Assert.That(SectorControlCVars.ClearingMinutes.DefaultValue, Is.EqualTo(10f));
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.ClearingOf(map, Far), Is.Null, "Held, not clearing.");

            Assert.That(territory.Free(map, Far), Is.True);
            until = STiming.CurTime + TimeSpan.FromSeconds(3);
            Assert.That(territory.ClearingOf(map, Far), Is.EqualTo(until));
            Assert.That(territory.ClearingOf(map, Next), Is.Null);
            Assert.That(territory.IsHeldOrClearing(new Robust.Shared.Map.MapCoordinates(CentreOf(Far), map)), Is.True);
            Assert.That(recorder.Clearing.Single(), Is.EqualTo(new WGSectorCellClearingEvent(map, Far, until, Holder)));
            Assert.That(recorder.Cleared, Is.Empty);

            var entry = Server.System<WGSectorSyncSystem>().Build(map).Clearing.Single();
            Assert.That((entry.Cell, entry.Until), Is.EqualTo((Far, until)), "The sync event carries it.");
        });

        await RunTicks(200);
        await Server.WaitAssertion(() =>
        {
            var territory = Server.System<WGSectorTerritorySystem>();
            var recorder = Server.System<WGSectorTestSystem>();
            var map = MapData.MapId;
            Assert.That(territory.ClearingOf(map, Far), Is.Null);
            Assert.That(territory.IsHeldOrClearing(new Robust.Shared.Map.MapCoordinates(CentreOf(Far), map)), Is.False);
            Assert.That(recorder.Cleared.Single(), Is.EqualTo(new WGSectorCellClearedEvent(map, Far)));
            Assert.That(Server.System<WGSectorSyncSystem>().Build(map).Clearing, Is.Empty);
        });
    }

    /// <summary>A cell claimed again while clearing leaves the state without a cleared event; zero minutes turns clearing off.</summary>
    [Test]
    public async Task ClaimLeavesClearingAndZeroTurnsItOff()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            ServerConfig.SetCVar(SectorControlCVars.ClearingMinutes, 10f);
            var territory = Server.System<WGSectorTerritorySystem>();
            var recorder = Server.System<WGSectorTestSystem>();
            var map = MapData.MapId;
            recorder.Reset();
            Assert.That(territory.TryClaim(map, Far, Holder, null), Is.True);
            Assert.That(territory.TryClaim(map, Next, Holder, null), Is.True);
            Assert.That(territory.Clear(map), Is.EqualTo(2));
            Assert.That(territory.ClearingOf(map, Far), Is.Not.Null, "Cleared by an admin counts as freed.");
            Assert.That(recorder.Clearing, Has.Count.EqualTo(2));

            Assert.That(territory.TryClaim(map, Far, Rival, null), Is.True);
            Assert.That(territory.ClearingOf(map, Far), Is.Null, "Claimed again.");
            Assert.That(territory.ClearingOf(map, Next), Is.Not.Null);
            Assert.That(recorder.Cleared, Is.Empty);
            Assert.That(Server.System<WGSectorSyncSystem>().Build(map).Clearing.Select(entry => entry.Cell), Is.EquivalentTo(new[] { Next }));

            ServerConfig.SetCVar(SectorControlCVars.ClearingMinutes, 0f);
            Assert.That(territory.Free(map, Far), Is.True);
            Assert.That(territory.ClearingOf(map, Far), Is.Null, "Off: freed at once.");
        });
    }

    /// <summary>The client draws a clearing cell with its countdown in place of the callsign, and the hover line says drones return.</summary>
    [Test]
    public async Task ClientShowsTheClearingCell()
    {
        await StartRound();
        TimeSpan until = default;
        await Server.WaitAssertion(() =>
        {
            ServerConfig.SetCVar(SectorControlCVars.ClearingMinutes, 10f);
            var territory = Server.System<WGSectorTerritorySystem>();
            Assert.That(territory.TryClaim(MapData.MapId, Far, Holder, null), Is.True);
            Assert.That(territory.Free(MapData.MapId, Far), Is.True);
            until = territory.ClearingOf(MapData.MapId, Far)!.Value;
        });

        await RunTicks(90);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.System<WGSectorClientSystem>().TryGetMap(MapData.MapId, out var view), Is.True);
            var clearing = view.Clearing.Single();
            Assert.That((clearing.Cell, clearing.Until), Is.EqualTo((Far, until)));
            Assert.That(clearing.Outline, Has.Length.EqualTo(12));
            Assert.That(clearing.Centre, Is.EqualTo(CentreOf(Far)));
            Assert.That(view.CellLabels.Select(label => label.Text), Does.Not.Contain(WGSectorHex.Callsign(Far)), "The countdown replaces the callsign.");
            Assert.That(view.CellLabels.Select(label => label.Text), Does.Contain(WGSectorHex.Callsign(Next)));

            var map = new WGSectorMapControl();
            var hover = map.Readout(view, CentreOf(Far));
            Assert.That(hover, Does.Contain("clearing, drones return in"));
            Assert.That(map.Readout(view, CentreOf(Next)), Does.Not.Contain("clearing"));
        });
    }
}
