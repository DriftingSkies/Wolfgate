#nullable enable annotations

using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.Cockpit;
using Content.Client._WG.Stylesheets;
using Content.Client.Shuttles.UI;
using Content.IntegrationTests.Tests._WG.CombatConsole;
using Content.IntegrationTests.Tests._WG.Shuttles;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.Shuttles;
using Content.Shared.Shuttles.Systems;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Cockpit;

/// <summary>Checks the cockpit's strategic map page and FTL lamp say when the ship has no working drive.</summary>
[TestFixture]
public sealed class WGCockpitFtlDriveTest
{
    [Test]
    public async Task MapPageWarnsAboveItsScrollingDetailsAndLocksTheButton()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(640, 700), new Vector2(560, 260) })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    var context = $"{skin}, {size}";
                    var lease = new WGCockpitLease();
                    using var screen = new MapScreen();
                    screen.WgCockpitMap(lease);
                    screen.Visible = true;
                    ui.WindowRoot.AddChild(screen);
                    var button = screen.FindControl<Button>("MapFTLButton");
                    var radar = screen.FindControl<ShuttleMapControl>("MapRadar");
                    var notice = screen.WgDriveNotice;

                    void Update(WGFtlDriveStatus drive)
                    {
                        screen.UpdateState(WGFtlDriveTestPages.State(FTLState.Available, drive));
                        WGFtlDriveTestPages.Layout(screen, size);
                    }

                    void AssertNotice(WGFtlDriveStatus drive)
                    {
                        WGFtlDriveTestPages.AssertDrive(screen, drive, context);
                        if (drive == WGFtlDriveStatus.Ready)
                            return;

                        WGFtlDriveTestPages.AssertWithin(notice, screen, context);
                        Assert.That(notice.Height, Is.GreaterThan(0), context);
                        Assert.That(WGCockpitLease.Descendants(screen).OfType<ScrollContainer>()
                                .Any(scroll => WGCockpitLease.Descendants(scroll).Contains(notice)), Is.False,
                            $"{context}: the notice must stay outside the scrolling details.");
                        Assert.That(radar.PixelHeight, Is.GreaterThan(0), $"{context}: the plot must keep room.");
                        Assert.That(notice.GlobalPosition.Y + notice.Height, Is.LessThanOrEqualTo(radar.GlobalPosition.Y + 1),
                            $"{context}: the notice sits above the plot.");
                    }

                    Update(WGFtlDriveStatus.Ready);
                    AssertNotice(WGFtlDriveStatus.Ready);
                    Assert.That(button.Disabled, Is.False, context);
                    WGButtonTestInput.Toggle(button, true);
                    Assert.That(radar.FtlMode, Is.True, context);

                    Update(WGFtlDriveStatus.Missing);
                    AssertNotice(WGFtlDriveStatus.Missing);
                    WGButtonTestInput.Click(button);
                    Assert.That(button.Pressed, Is.False, $"{context}: the FTL button must not toggle on without a drive.");
                    Assert.That(radar.FtlMode, Is.False, context);

                    Update(WGFtlDriveStatus.Unpowered);
                    AssertNotice(WGFtlDriveStatus.Unpowered);
                    Assert.That(notice.Text, Is.Not.EqualTo(Loc.GetString("shuttle-console-no-powered-ftl-drive")), context);
                    WGButtonTestInput.Click(button);
                    Assert.That(button.Pressed, Is.False, context);

                    Update(WGFtlDriveStatus.Ready);
                    AssertNotice(WGFtlDriveStatus.Ready);
                    Assert.That(button.Disabled, Is.False, context);

                    ui.SetHovered(null);
                    lease.Restore();
                    screen.Orphan();
                }
            }
            finally
            {
                settings.SetCVar(WolfgateCVars.UiStyle, originalSkin);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LeavingTheCockpitReturnsTheNoticeToTheHelmsDrivePanel()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            using var helm = new ShuttleConsoleWindow();
            var map = helm.FindControl<MapScreen>("MapContainer");
            var notice = map.WgDriveNotice;
            var panel = notice.Parent!;
            var index = notice.GetPositionInParent();
            var readout = panel.GetChild(index + 1);
            Assert.That(index, Is.EqualTo(0), "The helm puts the notice first in its drive panel.");
            helm.OpenCentered();
            helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Map);
            try
            {
                // The drive changes while the pilot is seated, so the notice must follow the status at exit, not at entry.
                foreach (var (atEntry, atExit) in new[]
                {
                    (WGFtlDriveStatus.Missing, WGFtlDriveStatus.Ready),
                    (WGFtlDriveStatus.Ready, WGFtlDriveStatus.Unpowered),
                })
                {
                    var context = $"{atEntry} then {atExit}";
                    map.UpdateState(WGFtlDriveTestPages.State(FTLState.Available, atEntry));
                    var lease = new WGCockpitLease();
                    map.WgCockpitMap(lease);
                    ui.WindowRoot.AddChild(map);
                    Assert.That(notice.Parent, Is.Not.SameAs(panel), $"{context}: the cockpit page takes the notice.");
                    map.UpdateState(WGFtlDriveTestPages.State(FTLState.Available, atExit));
                    WGFtlDriveTestPages.AssertDrive(map, atExit, $"{context}, seated");
                    lease.Restore();

                    Assert.That(notice.Parent, Is.SameAs(panel), $"{context}: the notice returns to the helm's drive panel.");
                    Assert.That(notice.GetPositionInParent(), Is.EqualTo(index), $"{context}: at its old place.");
                    Assert.That(panel.GetChild(index + 1), Is.SameAs(readout), $"{context}: still before the state readout.");
                    WGFtlDriveTestPages.AssertDrive(map, atExit, $"{context}, back at the helm");
                }
            }
            finally
            {
                helm.Close();
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FtlLampNeverReadsReadyWithoutAWorkingDrive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var entities = pair.Client.ResolveDependency<IEntityManager>();
            var ship = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            using var helm = new ShuttleConsoleWindow();
            var map = helm.FindControl<MapScreen>("MapContainer");
            map.SetShuttle(ship);
            var bank = helm.WgCockpitStatus();
            try
            {
                var lamp = bank.Children.OfType<WGCockpitStatusLamp>().Single(control => control.Name == "wg-cockpit-status-ftl");

                void AssertLamp(FTLState phase, WGFtlDriveStatus drive, WGCockpitLampState state, string detail)
                {
                    map.UpdateState(WGFtlDriveTestPages.State(phase, drive));
                    Assert.That(lamp.Reading, Is.EqualTo(new WGCockpitStatusReading(state, detail)), $"{phase}, {drive}");
                }

                AssertLamp(FTLState.Available, WGFtlDriveStatus.Ready, WGCockpitLampState.Good, "wg-cockpit-status-ftl-ready");
                AssertLamp(FTLState.Available, WGFtlDriveStatus.Missing, WGCockpitLampState.Fault, "shuttle-console-no-powered-ftl-drive");
                AssertLamp(FTLState.Available, WGFtlDriveStatus.Unpowered, WGCockpitLampState.Fault, "shuttle-console-ftl-drive-unpowered");
                AssertLamp(FTLState.Available, WGFtlDriveStatus.Ready, WGCockpitLampState.Good, "wg-cockpit-status-ftl-ready");
            }
            finally
            {
                bank.Teardown();
                entities.DeleteEntity(ship);
            }
        });
        await pair.CleanReturnAsync();
    }
}
