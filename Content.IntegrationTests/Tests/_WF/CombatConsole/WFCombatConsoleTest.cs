#nullable enable annotations

using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Reflection;
using System.Text;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Shared.Localization;
using Content.Client._WF.CombatConsole;
using Content.Client._WF.Planets;
using Content.Client._WF.Stylesheets;
using Content.Client._WF.ShipPa.UI;
using Content.Client._WF.ShipAccess;
using Content.Client._WF.ShipShields;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipPa;
using Content.Shared._WF.ShipShields;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Planets;
using Robust.Shared.Configuration;
using Content.Shared._WF.Shuttles;
using Content.Client._Mono.FireControl.UI;
using Content.Client.Shuttles.UI;
using Content.Client._WF.Shuttles.UI;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Maths;
using Content.Server._Mono.FireControl;
using Content.Server._Mono.Projectiles.TargetSeeking;
using Content.Server._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Content.Shared.Projectiles;
using Robust.Client.ResourceManagement;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.CombatConsole;

/// <summary>Checks instrument construction, console ownership and real missile-lock filtering.</summary>
[TestFixture]
public sealed class WFCombatConsoleTest
{
    [Test]
    public async Task ConsoleOwnershipAndExposedMissileLocks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid consoleUid = default;
        EntityUid missileUid = default;
        EntityUid serverUid = default;
        EntityUid flareUid = default;
        await pair.Server.WaitAssertion(() =>
        {
            consoleUid = em.SpawnEntity(null, map.GridCoords);
            serverUid = em.SpawnEntity(null, map.GridCoords);
            em.System<SharedTransformSystem>().AnchorEntity(consoleUid);
            var console = em.AddComponent<FireControlConsoleComponent>(consoleUid);
            var server = em.AddComponent<FireControlServerComponent>(serverUid);
            server.ConnectedGrid = map.Grid.Owner;
            server.ProcessingPower = 100;
            em.EnsureComponent<FireControlGridComponent>(map.Grid.Owner).ControllingServer = serverUid;
            server.Consoles.Add(consoleUid);

            console.ConnectedServer = serverUid;
            var settings = em.AddComponent<WFCombatConsoleComponent>(consoleUid);
            settings.Automatic = true;
            var system = em.System<WFCombatConsoleSystem>();
            Assert.That(system.TryGetServer(consoleUid, console, out _, out _), Is.True);
            server.Consoles.Clear();
            Assert.That(system.TryGetServer(consoleUid, console, out _, out _), Is.False);
            server.Consoles.Add(consoleUid);

            flareUid = em.SpawnEntity("WeaponTurretFlare", map.GridCoords);
            Assert.That(em.GetComponent<TransformComponent>(flareUid).Anchored, Is.True);
            em.GetComponent<FireControllableComponent>(flareUid).ControllingServer = serverUid;
            server.Controlled.Add(flareUid);
            var snapshot = system.GetState(consoleUid, console);
            Assert.That(snapshot.UnlimitedSupply, Is.True, "Sunny's existing autoloader must remain usable at zero stored rounds.");

            missileUid = em.SpawnEntity(null, new EntityCoordinates(map.MapUid, new Vector2(10, 0)));
            em.AddComponent<ProjectileComponent>(missileUid);
            var seeker = em.AddComponent<TargetSeekingComponent>(missileUid);
            seeker.CurrentTarget = map.Grid.Owner;
            seeker.DetectionRange = 0; // Isolate alert filtering from countermeasure reacquisition.
            seeker.Launched = true;
        });

        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.EqualTo(1));
            Assert.That(em.GetComponent<WFFlareLauncherComponent>(flareUid).NextBurst, Is.GreaterThan(TimeSpan.Zero),
                "Automatic defense must actually fire the existing Sunny launcher.");
            em.GetComponent<TargetSeekingComponent>(missileUid).ExposesTracking = false;
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.Zero);
            var seeker = em.GetComponent<TargetSeekingComponent>(missileUid);
            seeker.ExposesTracking = true;
            seeker.SeekingDisabled = true;
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.Zero);
            em.GetComponent<TargetSeekingComponent>(missileUid).SeekingDisabled = false;
            em.GetComponent<ProjectileComponent>(missileUid).Shooter = consoleUid;
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.Zero,
                "Own-ship projectiles must not waste flares.");
            em.GetComponent<ProjectileComponent>(missileUid).Shooter = null;
            em.System<SharedTransformSystem>().SetCoordinates(missileUid,
                new EntityCoordinates(map.MapUid, new Vector2(1000, 0)));
        });
        await pair.RunTicksSync(20);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WFCombatConsoleComponent>(consoleUid).Threats, Is.Zero);
            em.DeleteEntity(missileUid);
            em.DeleteEntity(flareUid);
            em.DeleteEntity(consoleUid);
            em.DeleteEntity(serverUid);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var fireTitle = Loc.GetString("wf-console-fire-title");
            using var gunnery = new FireControlWindow();
            using var helm = new ShuttleConsoleWindow();
            Assert.That(gunnery.MinHeight, Is.LessThan(800));
            Assert.That(helm.MinHeight, Is.LessThan(1000));
            var camera = helm.FindControl<ShuttleCameraBar>("CameraBar");
            Assert.That(camera.Visible, Is.True);
            foreach (var mode in Enum.GetValues<ShuttleConsoleWindow.ShuttleConsoleMode>())
            {
                helm.SwitchMode(mode);
                Assert.That(camera.Visible, Is.EqualTo(mode == ShuttleConsoleWindow.ShuttleConsoleMode.Nav),
                    "Camera controls belong only to Navigation.");
                helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Nav);
                Assert.That(camera.Visible, Is.True, "Returning to Navigation must restore its camera controls.");
            }
            var navigation = helm.FindControl<NavScreen>("NavContainer");
            var altitude = Descendants(navigation).OfType<WFGlassGauge>().Single(gauge => gauge.Name == "WfAltitudeGauge");
            var climb = Descendants(navigation).OfType<WFGlassGauge>().Single(gauge => gauge.Name == "WfClimbGauge");
            var travel = navigation.FindControl<Label>("GridTravelState").Parent!;
            Assert.That(altitude.Visible || climb.Visible || travel.Visible, Is.False,
                "A console without altitude data must not show vertical-flight instruments.");
            var clientMaps = pair.Client.ResolveDependency<IMapManager>();
            var flightMap = clientMaps.CreateMap();
            var flightGrid = clientMaps.CreateGrid(flightMap);
            var clientEntities = pair.Client.EntMan;
            var flightMapUid = clientMaps.GetMapEntityId(flightMap);
            var flightTransform = clientEntities.GetComponent<TransformComponent>(flightGrid.Owner);
            clientEntities.EnsureComponent<CEZPhysicsComponent>(flightGrid.Owner).Velocity = 3f;
            navigation.SetShuttle(flightGrid.Owner);
            void CheckFlightInstruments(bool visible)
            {
                typeof(NavScreen).GetMethod("UpdateAltitude", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(navigation, new object[] { flightTransform });
                Assert.That(altitude.Visible, Is.EqualTo(visible));
                Assert.That(climb.Visible, Is.EqualTo(visible));
                Assert.That(travel.Visible, Is.EqualTo(visible), "Hidden travel text must leave no empty glass panel.");
                if (visible)
                    Assert.That(climb.Reading.Value, Is.EqualTo(3f));
            }
            CheckFlightInstruments(false);
            clientEntities.AddComponent<CEZMapComponent>(flightMapUid).Depth = 2;
            CheckFlightInstruments(true);
            clientEntities.RemoveComponent<CEZMapComponent>(flightMapUid);
            clientEntities.AddComponent<CEZTransitMapComponent>(flightMapUid);
            CheckFlightInstruments(true);
            clientEntities.RemoveComponent<CEZTransitMapComponent>(flightMapUid);
            CheckFlightInstruments(false);
            navigation.SetShuttle(null);
            clientMaps.DeleteMap(flightMap);
            var shipScreen = helm.FindControl<ShipScreen>("ShipContainer");
            var shipCommands = 0;
            shipScreen.CodeRequested += _ => shipCommands++;
            shipScreen.GeneralQuartersRequested += _ => shipCommands++;
            shipScreen.AnnounceRequested += _ => shipCommands++;
            shipScreen.SoundRequested += _ => shipCommands++;
            shipScreen.SoundStopRequested += () => shipCommands++;
            shipScreen.CollisionAlertRequested += _ => shipCommands++;
            var shipSettings = pair.Client.ResolveDependency<IConfigurationManager>();
            var shipOriginalSkin = shipSettings.GetCVar(WolfgateCVars.UiStyle);
            var accessDoor = clientEntities.SpawnEntity(null, MapCoordinates.Nullspace);
            helm.OpenCentered();
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { new Vector2(960, 600), new Vector2(1180, 780) })
                {
                    shipSettings.SetCVar(WolfgateCVars.UiStyle, skin);
                    helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Ship);
                    helm.SetSize = size;
                    foreach (var control in Descendants(helm))
                        control.InvalidateMeasure();
                    helm.Measure(size);
                    helm.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    var mapView = shipScreen.FindControl<ShipViewControl>("ShipView");
                    var plot = Descendants(shipScreen).Single(control => control.Name == "WfHullPlot");
                    var status = Descendants(shipScreen).Single(control => control.Name == "WfHullStatus");
                    var controls = Descendants(shipScreen).Single(control => control.Name == "WfHullControls");
                    var announcements = Descendants(shipScreen).Single(control => control.Name == "WfHullAnnouncements");
                    var alarmPanel = Descendants(shipScreen).OfType<ShipAlarmPanel>().Single();
                    var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
                    Assert.That(camera.Visible, Is.False);
                    Assert.That(Descendants(shipScreen).OfType<ScrollContainer>().Where(scroll => scroll.Name != "QueueScroll"), Is.Empty,
                        "Ship status, overlays and PA controls must use the available width instead of a scrolling sidebar.");
                    Assert.That(plot.Width, Is.LessThan(shipScreen.Width * 0.5f), "The hull plot must leave most of the width for ship instruments and controls.");
                    Assert.That(status.GlobalPosition.X, Is.GreaterThanOrEqualTo(plot.GlobalPosition.X + plot.Width),
                        "Hull telemetry must sit beside the plot.");
                    Assert.That(announcements.GlobalPosition.X, Is.GreaterThanOrEqualTo(status.GlobalPosition.X + status.Width),
                        "Announcements must use a separate column beside telemetry.");
                    foreach (var panel in new[] { plot, status, controls, announcements })
                        AssertWithin(panel, shipScreen);
                    var departments = Descendants(shipScreen).OfType<CheckBox>().Single(toggle => toggle.Name == "DepartmentToggle");
                    Assert.That(departments.GlobalPosition.X, Is.GreaterThanOrEqualTo(mapView.GlobalPosition.X + mapView.Width),
                        "The department toggle must sit beside the hull plot, not over it.");
                    foreach (var name in new[] { "DamageToggle", "FireToggle", "PressureToggle", "PowerToggle", "FitButton" })
                        AssertWithin(Descendants(shipScreen).Single(control => control.Name == name), shipScreen);
                    AssertWithin(departments, shipScreen);
                    var gauges = Descendants(shipScreen).OfType<WFGlassGauge>().ToArray();
                    Assert.That(gauges, Has.Length.EqualTo(6));
                    foreach (var gauge in gauges)
                    {
                        AssertWithin(gauge, status);
                        Assert.That(gauge.Width, Is.GreaterThanOrEqualTo(85), "Hull gauges must retain a readable scale.");
                        Assert.That(gauge.Height, Is.GreaterThanOrEqualTo(100));
                    }
                    foreach (var name in new[] { "GeneralQuartersButton", "CollisionAlertButton", "AnnounceButton", "SoundButton", "SoundStopButton" })
                        AssertWithin(alarmPanel.FindControl<Button>(name), announcements);
                    foreach (var button in Descendants(alarmPanel.FindControl<BoxContainer>("CodeContainer")).OfType<Button>())
                        AssertWithin(button, announcements);
                    foreach (var name in new[] { "AnnounceEdit", "SoundEdit" })
                    {
                        var input = alarmPanel.FindControl<LineEdit>(name);
                        AssertWithin(input, announcements);
                        Assert.That(input.Width, Is.GreaterThanOrEqualTo(100), "The PA needs a usable input field at the minimum console size.");
                    }
                    foreach (var button in Descendants(shipScreen).OfType<Button>().Where(button => button.VisibleInTree))
                    {
                        AssertWithin(button, shipScreen);
                        AssertCaptionFits(button.Label, ui);
                    }
                    Assert.That(mapView.Width, Is.GreaterThan(200));
                    Assert.That(mapView.Height, Is.GreaterThan(150));
                    AssertWithin(mapView, shipScreen);
                    helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Access);
                    var access = helm.FindControl<ShipAccessScreen>("AccessContainer");
                    access.SetShuttle(null);
                    access.Refresh();
                    foreach (var control in Descendants(helm))
                        control.InvalidateMeasure();
                    helm.Measure(size);
                    helm.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    var accessPlot = Descendants(access).Single(control => control.Name == "WfAccessPlot");
                    var accessSettings = Descendants(access).Single(control => control.Name == "WfAccessSettings");
                    var accessPeople = Descendants(access).Single(control => control.Name == "WfAccessPeople");
                    var accessDoors = Descendants(access).Single(control => control.Name == "WfAccessDoors");
                    var accessMap = Descendants(access).Single(control => control.Name == "DoorMap");
                    Assert.That(Descendants(accessPlot).OfType<WFScreenBezel>()
                        .Any(bezel => Descendants(bezel).Contains(accessMap)), Is.True,
                        "The access map must use the instrument bezel in both console themes.");
                    Assert.That(accessPlot.Width, Is.GreaterThan(access.Width * 0.43f),
                        "The access diagram must remain the dominant panel at normal console sizes.");
                    foreach (var panel in new[] { accessSettings, accessPeople, accessDoors })
                        Assert.That(accessPlot.Width, Is.GreaterThan(panel.Width * 1.5f),
                            "Access controls must not take the diagram's dominant share of the page.");
                    Assert.That(accessSettings.GlobalPosition.X, Is.GreaterThanOrEqualTo(accessPlot.GlobalPosition.X + accessPlot.Width));
                    Assert.That(accessPeople.GlobalPosition.X, Is.GreaterThanOrEqualTo(accessPlot.GlobalPosition.X + accessPlot.Width));
                    Assert.That(accessDoors.GlobalPosition.X, Is.GreaterThanOrEqualTo(accessSettings.GlobalPosition.X + accessSettings.Width),
                        "Door rules must use the third column at normal console sizes.");
                    foreach (var panel in new[] { accessPlot, accessSettings, accessPeople, accessDoors })
                        AssertWithin(panel, access);
                    Assert.That(accessMap.Height, Is.GreaterThan(150));
                    AssertWithin(accessMap, accessPlot);
                    foreach (var caption in Descendants(access.FindControl<GridContainer>("LegendContainer")).OfType<Label>())
                    {
                        AssertWithin(caption, accessPlot);
                        AssertCaptionFits(caption, ui);
                    }
                    AssertWithin(access.FindControl<Label>("ReadOnlyLabel"), accessSettings);
                    PopulateAccessEditor(access, accessDoor);
                    foreach (var control in Descendants(helm))
                        control.InvalidateMeasure();
                    helm.Measure(size);
                    helm.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    Assert.That(access.FindControl<Label>("ReadOnlyLabel").Visible, Is.False);
                    foreach (var panel in new[] { accessSettings, accessPeople, accessDoors })
                        AssertWithin(panel, access);
                    var locked = access.FindControl<CheckBox>("LockedCheck");
                    AssertWithin(locked, accessSettings);
                    AssertCaptionFits(locked.Label, ui);
                    foreach (var prefix in new[] { "ShipCode", "DoorCode" })
                    {
                        var panel = prefix == "ShipCode" ? accessSettings : accessDoors;
                        var edit = access.FindControl<LineEdit>(prefix + "Edit");
                        AssertWithin(edit, panel);
                        Assert.That(edit.Width, Is.GreaterThanOrEqualTo(64), "A code input must fit all four digits.");
                        AssertCaptionFits(access.FindControl<Label>(prefix + "Label"), ui);
                        foreach (var action in new[] { "RevealButton", "SetButton", "ClearButton" })
                        {
                            var button = access.FindControl<Button>(prefix + action);
                            AssertWithin(button, panel);
                            AssertCaptionFits(button.Label, ui);
                        }
                    }
                    foreach (var name in new[] { "DoorRuleButton", "AllDoorsRuleButton" })
                    {
                        var rule = access.FindControl<OptionButton>(name);
                        AssertWithin(rule, accessDoors);
                        foreach (var caption in Descendants(rule).OfType<Label>().Where(label => label.VisibleInTree))
                            AssertCaptionFits(caption, ui);
                    }
                    var applyRules = access.FindControl<ConfirmButton>("AllDoorsApplyButton");
                    AssertWithin(applyRules, accessDoors);
                    AssertCaptionFits(applyRules.Label, ui);
                    AssertCaptionFits(applyRules.Label, ui, applyRules.ConfirmationText);
                    var allowedList = Descendants(access).OfType<WFAccessListRegion>().Single(control => control.Name == "WfAccessAllowedList");
                    var nearbyList = Descendants(access).OfType<WFAccessListRegion>().Single(control => control.Name == "WfAccessNearbyList");
                    var doorList = Descendants(access).OfType<WFAccessListRegion>().Single(control => control.Name == "WfAccessDoorList");
                    foreach (var (list, contentName) in new[]
                    {
                        (allowedList, "AllowListContainer"),
                        (nearbyList, "NearbyContainer"),
                        (doorList, "DoorPlayersContainer"),
                    })
                    {
                        AssertWithin(list, list == doorList ? accessDoors : accessPeople);
                        var firstRow = access.FindControl<BoxContainer>(contentName).Children.First();
                        var context = $"{skin}, {size}, {list.Name}: access={access.Height}, settings={accessSettings.Height}, " +
                            $"people={accessPeople.Height}, allowed={allowedList.Height}, nearby={nearbyList.Height}, " +
                            $"row desired={firstRow.DesiredSize.Y}, actual={firstRow.Height}";
                        Assert.That(firstRow.Height, Is.GreaterThan(0), context);
                        Assert.That(list.Height + 1, Is.GreaterThanOrEqualTo(Math.Max(firstRow.DesiredSize.Y, firstRow.Height)),
                            $"A populated access list must show at least one complete row. {context}");
                    }
                    foreach (var button in Descendants(allowedList).OfType<Button>().Where(button => button.VisibleInTree))
                        AssertCaptionFits(button.Label, ui);
                    foreach (var check in Descendants(allowedList).OfType<CheckBox>().Where(check => check.VisibleInTree))
                        AssertCaptionFits(check.Label, ui);
                    Assert.That(allowedList.Height + nearbyList.Height, Is.GreaterThan(accessPeople.Height * 0.55f),
                        $"Crew lists should occupy most of their dedicated column: {skin}, {size}, people={accessPeople.Height}, allowed={allowedList.Height}, nearby={nearbyList.Height}.");
                }
                Assert.That(shipCommands, Is.Zero, "Changing theme or layout must not transmit PA or alarm commands.");
            }
            finally
            {
                helm.Close();
                clientEntities.DeleteEntity(accessDoor);
                shipSettings.SetCVar(WolfgateCVars.UiStyle, shipOriginalSkin);
            }
            var gun = new NetEntity(710);
            FireControlConsoleBoundInterfaceState GunState(bool connected, int? ammo) => new(connected,
                new[] { new FireControllableEntry(gun, default, "Gauge test weapon", ammo, true) },
                new Content.Shared.Shuttles.BUIStates.NavInterfaceState(250, null, null, new(), default));
            var armed = GunState(true, 240);
            armed.Combat.FlareLaunchers.Add(new NetEntity(711));
            armed.Combat.Ammunition = 18;
            armed.Combat.Threats = 2;
            var countermeasures = Descendants(gunnery).Single(control => control.Name == "WfCountermeasurePanel");
            Assert.That(countermeasures.Visible, Is.False);
            gunnery.UpdateStatus(armed);
            Assert.That(countermeasures.Visible, Is.True);
            var meter = gunnery.WeaponsList[gun].Children.OfType<WFWeaponRow>().Single();
            Assert.That(meter.Reading.Value, Is.EqualTo(240));
            Assert.That(meter.Reading.Maximum, Is.GreaterThanOrEqualTo(240));
            gunnery.OpenCentered();
            foreach (var size in new[] { new Vector2(960, 600), new Vector2(960, 640), new Vector2(1180, 780) })
            {
                gunnery.SetSize = size;
                gunnery.Measure(size);
                gunnery.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(meter.Width, Is.GreaterThan(100), "Weapon names and supply must retain a readable full-width row.");
                Assert.That(meter.GlobalPosition.X + meter.Width, Is.LessThanOrEqualTo(gunnery.GlobalPosition.X + size.X));
                var battery = Descendants(gunnery).Single(control => control.Name == "WfWeaponBattery");
                Assert.That(Descendants(battery).OfType<ScrollContainer>(), Is.Empty);
                Assert.That(gunnery.WeaponsList[gun].Height, Is.InRange(48, 56), "Sparse batteries retain the same readable row height.");
                var dispense = Descendants(gunnery).OfType<Button>().Single(button => button.HasStyleClass("WfDispense"));
                AssertWithin(dispense, countermeasures);
                AssertWithin(countermeasures, gunnery);
                AssertWithin(meter, gunnery);
                Assert.That(countermeasures.Height, Is.LessThan(130), "The flare bank must leave room for the tactical plot.");
                Assert.That(countermeasures.GlobalPosition.Y + countermeasures.Height,
                    Is.LessThanOrEqualTo(gunnery.GlobalPosition.Y + gunnery.Height));
                Assert.That(countermeasures.GlobalPosition.X + countermeasures.Width,
                    Is.LessThanOrEqualTo(gunnery.GlobalPosition.X + gunnery.Width));
            }
            gunnery.UpdateStatus(GunState(true, 0));
            Assert.That(countermeasures.Visible, Is.False, "Removing the last launcher must hide the entire flare panel.");
            Assert.That(meter.Reading.Value, Is.Zero, "Empty ammunition is a real zero.");
            gunnery.UpdateStatus(GunState(false, 240));
            Assert.That(meter.Reading.Value, Is.Null, "A disconnected console must not retain a live needle.");
            gunnery.UpdateStatus(GunState(true, null));
            Assert.That(meter.Reading.Value, Is.Null, "Unknown ammo capacity must not be displayed as an empty magazine.");
            var hull = helm.FindControl<ShipScreen>("ShipContainer");
            hull.UpdateStatus(new ShipStatusMessage(null, new(), new ShipStatusSummary { WorstIntegrity = 0.85f, DamagedTiles = 7 }));
            var hullMeters = Descendants(hull).OfType<WFGlassGauge>().ToArray();
            Assert.That(hullMeters.Any(gauge => Math.Abs((gauge.Reading.Value ?? -100) - 85) < 0.001), Is.True);
            Assert.That(hullMeters.Any(gauge => gauge.Reading.Value == 7), Is.True);
            hull.SetShuttle(null);
            Assert.That(hullMeters.Any(gauge => gauge.Reading.Value == 7), Is.True,
                "A repeated shuttle assignment must not blank the latest telemetry sweep.");
            hull.ClearStatus();
            Assert.That(hullMeters.All(gauge => gauge.Reading.Value == null), Is.True, "Cleared telemetry must blank every hull instrument.");
            var cfg = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = cfg.GetCVar(WolfgateCVars.UiStyle);
            var requests = 0;
            gunnery.CombatMessage += _ => requests++;
            gunnery.UpdateStatus(armed);
            gunnery.WeaponsList[gun].Pressed = true;
            gunnery.OpenCentered();
            helm.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Nav);
            helm.OpenCentered();
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro, WolfgateSkins.Futurist, WolfgateSkins.Retro })
                {
                    cfg.SetCVar(WolfgateCVars.UiStyle, skin.Id);
                    Assert.That(WFInstrumentTheme.Skin, Is.SameAs(skin));
                    Assert.That(WFInstrumentTheme.Digital, Is.EqualTo(skin == WolfgateSkins.Futurist));
                    Assert.That(Descendants(gunnery).OfType<Label>().Single(label => label.Text == fireTitle).FontColorOverride,
                        Is.EqualTo(skin.Accent), "Open labels must change palette with the selected theme.");
                    Assert.That(gunnery.WeaponsList[gun].Children.OfType<WFWeaponRow>().Single(), Is.SameAs(meter),
                        "A style switch must retain the same controls and ammunition binding.");
                    Assert.That(meter.Reading.Value, Is.EqualTo(240));
                    Assert.That(gunnery.WeaponsList[gun].Pressed, Is.True);
                    Assert.That(gunnery.WeaponsList[gun].Height, Is.InRange(48, 56),
                        "The row must remain readable without expanding into a large ammunition gauge.");
                    foreach (var console in new Control[] { gunnery, helm })
                    {
                        var size = new Vector2(960, 600);
                        console.SetSize = size;
                        console.Measure(size);
                        console.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                        Assert.That(console.VisibleInTree, Is.True);
                        var visiblePlots = Descendants(console).OfType<ShuttleNavControl>()
                            .Where(plot => plot.VisibleInTree).ToArray();
                        Assert.That(visiblePlots, Is.Not.Empty, "The layout check must exercise an actual visible console plot.");
                        foreach (var plot in visiblePlots)
                        {
                            AssertWithin(plot, console);
                            Assert.That(plot.Width, Is.GreaterThan(150));
                            Assert.That(plot.Height, Is.GreaterThan(150));
                        }
                        var visibleButtons = Descendants(console).OfType<Button>()
                            .Where(button => button.VisibleInTree).ToArray();
                        Assert.That(visibleButtons, Is.Not.Empty);
                        foreach (var button in visibleButtons)
                        {
                            Assert.That(button.Width, Is.GreaterThan(0), button.Name);
                            Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(console.GlobalPosition.X - 1), button.Name);
                            Assert.That(button.GlobalPosition.X + button.Width,
                                Is.LessThanOrEqualTo(console.GlobalPosition.X + console.Width + 1), button.Name);
                        }
                        if (console != gunnery)
                            continue;
                        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
                        AssertCaptionFits(gunnery.FindControl<Label>("ServerStatus"), ui);
                        foreach (var name in new[] { "IFFToggle", "IFFDetailedToggle", "DockToggle" })
                            AssertCaptionFits(gunnery.FindControl<Button>(name).Label, ui);
                    }
                }
                Assert.That(requests, Is.Zero, "Changing a theme must not fire weapons, save groups or toggle automatic flares.");
                gunnery.Close();
                cfg.SetCVar(WolfgateCVars.UiStyle, WolfgateSkins.Futurist.Id);
                gunnery.OpenCentered();
                Assert.That(Descendants(gunnery).OfType<Label>().Single(label => label.Text == fireTitle).FontColorOverride,
                    Is.EqualTo(WolfgateSkins.Futurist.Accent), "A closed console must adopt the current skin when reopened.");
            }
            finally
            {
                gunnery.Close();
                helm.Close();
                cfg.SetCVar(WolfgateCVars.UiStyle, originalSkin);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleCuesDecode()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var resources = pair.Client.ResolveDependency<IResourceCache>();
            foreach (var cue in new[] { "key", "switch_on", "switch_off", "selector", "bearing", "warning" })
            {
                var clip = resources.GetResource<AudioResource>($"/Audio/_WF/CombatConsole/HighFleet/{cue}.ogg").AudioStream;
                Assert.That(clip.Length.TotalSeconds, Is.GreaterThan(0), $"Console cue {cue} must decode to a non-empty clip.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ShipAlertCodeButtonsKeepTheirCodeColours()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var protos = pair.Client.ResolveDependency<IPrototypeManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            using var helm = new ShuttleConsoleWindow();
            var panel = helm.FindControl<ShipScreen>("ShipContainer").FindControl<ShipAlarmPanel>("AlarmPanel");
            var refresh = typeof(ShipAlarmPanel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var buttons = Descendants(panel.FindControl<BoxContainer>("CodeContainer")).OfType<Button>().ToArray();
            var frame = typeof(ShipAlarmPanel).GetMethod("FrameUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void AssertCodeColours(string reason)
            {
                foreach (var proto in protos.EnumeratePrototypes<ShipAlertCodePrototype>().Where(proto => proto.Selectable))
                {
                    var button = buttons.Single(candidate => candidate.Text == Loc.GetString(proto.Name));
                    Assert.That(button.Label.FontColorOverride, Is.EqualTo(proto.Color), reason);
                }
            }
            Assert.That(buttons, Is.Not.Empty);
            // The first frame comes before the first poll, so it has to be the one that colours the buttons.
            frame.Invoke(panel, new object[] { new FrameEventArgs(0f) });
            AssertCodeColours("A new console shows the code colours on its first frame.");
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro, WolfgateSkins.Futurist })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin.Id);
                    WFInstrumentTheme.Apply(helm);
                    frame.Invoke(panel, new object[] { new FrameEventArgs(0f) });
                    AssertCodeColours($"{skin.Id}: a restyle must not leave a frame without the code colours.");
                    WFInstrumentTheme.Apply(helm);
                    refresh.Invoke(panel, new object[] { null });
                    foreach (var proto in protos.EnumeratePrototypes<ShipAlertCodePrototype>().Where(proto => proto.Selectable))
                    {
                        var button = buttons.Single(candidate => candidate.Text == Loc.GetString(proto.Name));
                        Assert.That(button.Label.FontColorOverride, Is.EqualTo(proto.Color),
                            $"{skin.Id}: the situation code button must keep its code colour.");
                    }
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
    public async Task AccessScreenExplainsDoorRulesToCrewWhoCannotEditThem()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var entities = pair.Client.EntMan;
            using var helm = new ShuttleConsoleWindow();
            var access = helm.FindControl<ShipAccessScreen>("AccessContainer");
            var door = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                var state = new WFShipAccessComponent { OwnerName = "Layout test captain", Locked = true };
                var node = new ShipAccessDoorNode(door, "Forward compartment airlock", new Vector2(3, -2), WFDoorAccessRule.PlayersOrCode, true);
                var rule = new WFDoorAccessRuleComponent { Rule = WFDoorAccessRule.PlayersOrCode };
                var rebuild = typeof(ShipAccessScreen).GetMethod("RebuildDoor", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var hint = access.FindControl<RichTextLabel>("DoorRuleHint");
                rebuild.Invoke(access, new object[] { state, node, rule, false });
                Assert.That(hint.Visible, Is.True, "Crew who cannot edit the rule still need its description.");
                rebuild.Invoke(access, new object[] { state, node, rule, true });
                Assert.That(hint.Visible, Is.False, "The owner reads the description from the rule selector's tooltip.");
                foreach (var name in new[] { "ReadOnlyLabel", "OwnerLabel", "DoorNameLabel", "DoorRuleLabel", "CodeAlertLabel" })
                    Assert.That(access.FindControl<Label>(name).MouseFilter, Is.Not.EqualTo(Control.MouseFilterMode.Ignore),
                        $"{name} carries a tooltip, so it must be hoverable.");
            }
            finally
            {
                entities.DeleteEntity(door);
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Upstream controls the refit hooks replace must be listed here; any other named control must stay in the tree.</summary>
    [Test]
    public async Task RecomposedConsolesKeepEveryNamedControl()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            using var gunnery = new FireControlWindow();
            using var helm = new ShuttleConsoleWindow();
            var none = Array.Empty<string>();
            var consoles = new (Control Root, string[] Dropped)[]
            {
                (gunnery, new[] { "RootBox", "ControlsBox", "WeaponsLabel", "RadarContainer" }),
                (helm, none),
                (helm.FindControl<NavScreen>("NavContainer"), new[] { "RightDisplayNav", "NavSettingsLabel" }),
                (helm.FindControl<ShipScreen>("ShipContainer"), none),
                (helm.FindControl<ShipScreen>("ShipContainer").FindControl<ShipAlarmPanel>("AlarmPanel"), none),
                (helm.FindControl<ShipAccessScreen>("AccessContainer"), none),
                (helm.FindControl<MapScreen>("MapContainer"), new[] { "RightDisplayMap", "MapDisplayLabel", "SettingsLabel", "HyperspaceLabel" }),
                (helm.FindControl<DockingScreen>("DockContainer"), new[] { "RightDisplayDock" }),
            };
            var lost = new List<string>();
            foreach (var (root, dropped) in consoles)
            {
                var scope = root.NameScope ?? throw new InvalidOperationException($"{root.GetType().Name} has no XAML name scope.");
                var named = (Dictionary<string, Control>) typeof(NameScope)
                    .GetField("_inner", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scope)!;
                foreach (var name in dropped)
                    Assert.That(named, Does.ContainKey(name), $"{root.GetType().Name}: the allow-list names a control that no longer exists.");
                foreach (var (name, control) in named)
                {
                    if (!dropped.Contains(name) && (control.Disposed || !IsUnder(control, root)))
                        lost.Add($"{root.GetType().Name}.{name}");
                }
            }
            Assert.That(lost, Is.Empty, "A refit hook disposed or orphaned these named upstream controls. Re-home each one, or list it " +
                "as deliberately dropped above with the reason: " + string.Join(", ", lost));
        });
        await pair.CleanReturnAsync();
    }

    private static bool IsUnder(Control control, Control root)
    {
        for (var current = control; current != null; current = current.Parent)
        {
            if (current == root)
                return true;
        }
        return false;
    }

    /// <summary>The flight deck caption and the cockpit key share the title bar, and each helm page fits its window without a scroll bar.</summary>
    [Test]
    public async Task HelmHeaderHoldsTheCockpitKeyAndPagesFitWithoutScrolling()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            using var helm = new ShuttleConsoleWindow();
            var title = Loc.GetString("wf-console-helm-title");
            Assert.That(helm.Title, Is.EqualTo(title));
            var captions = Descendants(helm).OfType<Label>().Where(label => label.Text == title).ToArray();
            Assert.That(captions, Has.Length.EqualTo(1), "The flight deck caption is the window title; the body has no title row of its own.");
            var header = captions[0].Parent!;
            var close = Descendants(helm).OfType<TextureButton>().Single(button =>
                button.HasStyleClass(Robust.Client.UserInterface.CustomControls.DefaultWindow.StyleClassWindowCloseButton));
            var cockpit = Descendants(helm).OfType<Button>().Single(button => button.Text == Loc.GetString("wf-cockpit-enter"));
            Assert.That(close.Parent, Is.SameAs(header));
            Assert.That(cockpit.Parent, Is.SameAs(header), "Enter Cockpit belongs in the title bar beside the window controls.");
            Assert.That(cockpit.GetPositionInParent(), Is.LessThan(close.GetPositionInParent()));
            Assert.That(cockpit.Disabled, Is.True, "Without a seat on the ship the key stays disabled.");
            Assert.That(cockpit.ToolTip, Is.EqualTo(Loc.GetString("wf-cockpit-seat-required")));
            var pressed = (Action<BaseButton.ButtonEventArgs>?) typeof(BaseButton)
                .GetField("OnPressed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cockpit);
            Assert.That(pressed?.GetInvocationList().Any(handler => ReferenceEquals(handler.Target, helm)), Is.True,
                "Enter Cockpit must still reach the cockpit controller for this window.");
            var modes = helm.FindControl<BoxContainer>("ModeButtons");
            Assert.That(modes.Parent!.Children.TakeWhile(child => child != modes)
                .All(child => child.Name is "CollisionBanner" or "CaptureBanner"), Is.True,
                "Only the hidden warning banners may sit above the page tabs.");

            helm.UpdateShieldShuntSnapshot(new WFShipShieldShuntState(true, true, 0.6f, 0.8f, 0.2f, MathF.PI / 2f));
            var navigation = helm.FindControl<NavScreen>("NavContainer");
            var position = navigation.FindControl<Label>("GridPosition");
            position.Text = "-12345.6, -12345.6";
            var contents = helm.FindControl<Control>("Contents");

            // The fullest Navigation page: a z-level map's altitude, climb and travel readouts, and a ship in planet orbit.
            var entities = pair.Client.EntMan;
            var orbitConsole = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var orbit = entities.AddComponent<WFConsoleOrbitTargetComponent>(orbitConsole);
            var orbitButton = navigation.FindControl<WFOrbitButton>("WfOrbitButton");
            orbitButton.SetConsole(orbitConsole);
            var orbitUpdate = typeof(WFOrbitButton).GetMethod("FrameUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void SetFlight(bool flying)
            {
                foreach (var name in new[] { "GridAltitude", "GridVerticalVelocity", "GridTravelState" })
                    navigation.FindControl<Label>(name).Visible = flying;
                navigation.FindControl<Label>("GridTravelState").Text = flying ? "Atmospheric flight" : string.Empty;
                orbit.InOrbit = flying;
                orbit.PlanetName = flying ? "Layout test planet" : string.Empty;
                orbit.Planet = flying ? new NetEntity(900) : null;
                orbit.LiftRatio = 0.8f;
                orbitUpdate.Invoke(orbitButton, new object[] { new FrameEventArgs(0) });
            }

            helm.OpenCentered();
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                foreach (var size in new[] { ShuttleConsoleWindow.WfDefaultSize, new Vector2(1896, 1056) })
                foreach (var (mode, flying) in new[]
                {
                    (ShuttleConsoleWindow.ShuttleConsoleMode.Nav, false), (ShuttleConsoleWindow.ShuttleConsoleMode.Nav, true),
                    (ShuttleConsoleWindow.ShuttleConsoleMode.Ship, false), (ShuttleConsoleWindow.ShuttleConsoleMode.Map, false),
                    (ShuttleConsoleWindow.ShuttleConsoleMode.Shields, false),
                })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    SetFlight(flying);
                    helm.SwitchMode(mode);
                    helm.SetSize = size;
                    Layout(helm, size);
                    var context = $"{skin}, {size}, {mode}{(flying ? ", z-level map and planet orbit" : string.Empty)}";
                    AssertWithin(cockpit, helm);
                    AssertCaptionFits(cockpit.Label, ui);
                    var scrolling = Descendants(helm).OfType<ScrollContainer>()
                        .Where(control => control.VisibleInTree && NeedsScroll(control))
                        .Select(scroll => $"{scroll.Name ?? scroll.GetType().Name} at x={scroll.GlobalPosition.X} holds " +
                            $"{scroll.Children.First(child => child is not ScrollBar).DesiredSize.Y} in {scroll.Height}: " +
                            HeightDump(scroll.Children.First(child => child is not ScrollBar), 4));
                    Assert.That(scrolling, Is.Empty, $"{context}: a page must fit the window without a scroll bar.");
                    AssertNothingClipped(contents, context);
                    if (mode == ShuttleConsoleWindow.ShuttleConsoleMode.Nav)
                    {
                        var coordinates = Descendants(navigation).Single(control => control.Name == "WfCoordinates");
                        var column = coordinates.Parent!.Children.ToList();
                        var index = column.IndexOf(coordinates);
                        var before = (WFGlassGauge) column[index - 1];
                        var after = (WFGlassGauge) column[index + 1];
                        Assert.That(before.Strip && after.Strip, Is.True, context);
                        Assert.That(before.GlobalPosition.Y + before.Height, Is.LessThanOrEqualTo(coordinates.GlobalPosition.Y + 1), context);
                        Assert.That(coordinates.GlobalPosition.Y + coordinates.Height, Is.LessThanOrEqualTo(after.GlobalPosition.Y + 1),
                            $"{context}: the coordinates box sits between the longitudinal and lateral gauges.");
                        Assert.That(IsUnder(position, coordinates), Is.True, context);
                        AssertWithin(coordinates, navigation);
                        AssertCaptionFits(position, ui);
                        foreach (var toggle in new[] { "IFFToggle", "IFFDetailedToggle", "DockToggle" })
                            AssertCaptionFits(navigation.FindControl<Button>(toggle).Label, ui);
                    }
                    if (mode == ShuttleConsoleWindow.ShuttleConsoleMode.Shields)
                    {
                        var screen = Descendants(helm).OfType<WFShipShieldShuntScreen>().Single();
                        var panels = Descendants(screen).Single(control => control.GetType().Name == "ShieldSettings")
                            .Children.Where(child => child.Visible).ToArray();
                        Assert.That(panels, Has.Length.EqualTo(3), context);
                        Assert.That(panels[1].GlobalPosition.X, Is.EqualTo(panels[0].GlobalPosition.X).Within(1), context);
                        Assert.That(panels[2].GlobalPosition.X, Is.GreaterThanOrEqualTo(panels[0].GlobalPosition.X + panels[0].Width),
                            $"{context}: a wide window gives coverage its own column.");
                        foreach (var panel in panels)
                            AssertWithin(panel, screen);
                    }
                }
            }
            finally
            {
                helm.Close();
                settings.SetCVar(WolfgateCVars.UiStyle, originalSkin);
                SetFlight(false);
                orbitButton.SetConsole(null);
                entities.DeleteEntity(orbitConsole);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ThemeChangesPreserveStatusColorsAndWeaponLayoutsSettle()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var settings = pair.Client.ResolveDependency<IConfigurationManager>();
            var originalSkin = settings.GetCVar(WolfgateCVars.UiStyle);
            using var status = new BoxContainer { SetSize = new Vector2(320, 80) };
            var danger = new StyleBoxFlat { BackgroundColor = Color.FromHex("#9d2828") };
            var authorized = new StyleBoxFlat { BackgroundColor = Color.FromHex("#23743d") };
            var panel = new PanelContainer { PanelOverride = danger, MinSize = new Vector2(100, 40) };
            var button = new Button { Text = "Access granted", ToggleMode = true, Pressed = true, StyleBoxOverride = authorized };
            status.AddChild(panel);
            status.AddChild(button);
            ui.WindowRoot.AddChild(status);
            var commands = 0;
            button.OnPressed += _ => commands++;
            WFInstrumentTheme.Install(status);
            using var window = new FireControlWindow();
            window.CombatMessage += _ => commands++;
            window.OpenCentered();
            window.SetSize = new Vector2(960, 600);
            var grid = Descendants(window).OfType<WFWeaponGrid>().Single();
            var frame = ui.GetType().GetMethod("FrameUpdate")!;
            void PumpLayout() => frame.Invoke(ui, new object[] { new FrameEventArgs(0) });
            try
            {
                foreach (var skin in new[] { WolfgateSkins.Retro.Id, WolfgateSkins.Futurist.Id })
                {
                    settings.SetCVar(WolfgateCVars.UiStyle, skin);
                    Assert.That(panel.PanelOverride, Is.SameAs(danger), "Theme changes must preserve the source panel's warning status.");
                    Assert.That(button.StyleBoxOverride, Is.SameAs(authorized), "Theme changes must preserve explicit access/department button colors.");
                    Assert.That(button.Pressed, Is.True);
                    for (var count = 13; count <= 32; count++)
                    {
                        var entries = Enumerable.Range(1, count).Select(index => new FireControllableEntry(new NetEntity(1200 + index),
                            default, $"Layout weapon {index}", 6, true)).ToArray();
                        window.UpdateStatus(new FireControlConsoleBoundInterfaceState(true, entries,
                            new Content.Shared.Shuttles.BUIStates.NavInterfaceState(250, null, null, new(), default)));
                        var selected = window.WeaponsList[entries[^1].NetEntity];
                        selected.Pressed = true;
                        for (var pass = 0; pass < 3; pass++)
                            PumpLayout();
                        Assert.That(grid.PageCount, Is.GreaterThan(1));
                        grid.SetPage(grid.PageCount - 1);
                        for (var pass = 0; pass < 3; pass++)
                            PumpLayout();
                        var lastPage = grid.PageIndex;
                        var visible = grid.Children.OfType<Button>().Where(item => item.VisibleInTree).ToArray();
                        Assert.That(visible, Does.Contain(selected), $"{skin}, {count} weapons: the final row must be reachable.");
                        var geometry = visible.Select(item => (item.Position, item.Size)).ToArray();
                        for (var frameIndex = 0; frameIndex < 3; frameIndex++)
                        {
                            PumpLayout();
                            var context = $"{skin}, {count} weapons, settled frame {frameIndex}";
                            Assert.That(grid.IsMeasureValid && grid.IsArrangeValid, Is.True,
                                $"{context}: an unchanged battery must not continually invalidate its layout.");
                            Assert.That(grid.PageIndex, Is.EqualTo(lastPage), context);
                            Assert.That(grid.Children.OfType<Button>().Where(item => item.VisibleInTree), Is.EqualTo(visible), context);
                            Assert.That(visible.Select(item => (item.Position, item.Size)), Is.EqualTo(geometry), context);
                            Assert.That(selected.Pressed, Is.True, context);
                        }
                    }
                }
                Assert.That(commands, Is.Zero, "Applying skins and settling layout must not transmit controls.");
            }
            finally
            {
                window.Close();
                status.Parent?.RemoveChild(status);
                settings.SetCVar(WolfgateCVars.UiStyle, originalSkin);
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Populates existing refresh paths with owner and selected-door data for layout checks.</summary>
    private static void PopulateAccessEditor(ShipAccessScreen screen, EntityUid door)
    {
        var state = new WFShipAccessComponent { OwnerName = "Layout test captain", Locked = true };
        for (uint index = 0; index < 12; index++)
            state.AllowList.Add(new WFShipAccessEntry
            {
                Key = new WFShipAccessKey(new NetEntity(900), index),
                Name = $"Crew member {index + 1}",
                Label = "Engineering",
                Builder = index % 2 == 0,
            });
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ShipAccessScreen).GetMethod("RebuildList", flags)!.Invoke(screen, new object[] { state, true });
        var rebuildNearby = typeof(ShipAccessScreen).GetMethod("RebuildNearby", flags)!;
        var nearbyType = rebuildNearby.GetParameters()[1].ParameterType;
        var nearby = (System.Collections.IList) Activator.CreateInstance(nearbyType)!;
        var personType = nearbyType.GenericTypeArguments[0];
        for (var index = 0; index < 12; index++)
            nearby.Add(Activator.CreateInstance(personType, new object[] { door, $"Nearby crew {index + 1}", true, true }));
        rebuildNearby.Invoke(screen, new object[] { true, nearby });
        var node = new ShipAccessDoorNode(door, "Forward compartment airlock", new Vector2(3, -2), WFDoorAccessRule.PlayersOrCode, true);
        typeof(ShipAccessScreen).GetMethod("RebuildDoor", flags)!
            .Invoke(screen, new object[] { state, node, new WFDoorAccessRuleComponent { Rule = WFDoorAccessRule.PlayersOrCode }, true });
        screen.FindControl<OptionButton>("AllDoorsRuleButton").SelectId((int) WFDoorAccessRule.PlayersOrCode);
        foreach (var prefix in new[] { "ShipCode", "DoorCode" })
        {
            screen.FindControl<LineEdit>(prefix + "Edit").Text = "1234";
            screen.FindControl<Label>(prefix + "Label").Text = Loc.GetString("ship-access-code-none");
        }
    }

    private static void Layout(Control root, Vector2 size)
    {
        foreach (var control in Descendants(root))
            control.InvalidateMeasure();
        root.Measure(size);
        root.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
    }

    /// <summary>A scroll container needs its bar when it shows a bar or its content is taller than it is.</summary>
    private static bool NeedsScroll(ScrollContainer scroll)
    {
        var content = scroll.Children.First(child => child is not ScrollBar);
        return scroll.Children.OfType<VScrollBar>().Any(bar => bar.Visible) || content.DesiredSize.Y > scroll.Height + 1;
    }

    /// <summary>The desired heights down a control tree, for a failure message that says where the room went.</summary>
    private static string HeightDump(Control control, int depth)
    {
        var text = $"{control.Name ?? control.GetType().Name} {control.DesiredSize.Y:0.#}";
        if (depth == 0 || control.ChildCount == 0)
            return text;
        return $"{text} [{string.Join(", ", control.Children.Where(child => child.Visible).Select(child => HeightDump(child, depth - 1)))}]";
    }

    /// <summary>Nothing on a page hangs past its container, where its clip would hide it without any scroll bar to show it.</summary>
    private static void AssertNothingClipped(Control page, string context)
    {
        var bottom = page.GlobalPosition.Y + page.Height + 1;
        var right = page.GlobalPosition.X + page.Width + 1;
        foreach (var control in Descendants(page).Skip(1).Where(control => control.VisibleInTree && control.Width > 0 && control.Height > 0))
        {
            var chain = new StringBuilder();
            for (var parent = control; parent != null && parent != page; parent = parent.Parent)
                chain.Append(parent.Name ?? parent.GetType().Name).Append(" < ");
            Assert.That(control.GlobalPosition.Y + control.Height, Is.LessThanOrEqualTo(bottom),
                $"{context}: {chain}is cut off at the bottom of the page.");
            Assert.That(control.GlobalPosition.X + control.Width, Is.LessThanOrEqualTo(right),
                $"{context}: {chain}is cut off at the right of the page.");
        }
    }

    private static void AssertWithin(Control control, Control parent)
    {
        Assert.That(control.VisibleInTree, Is.True, $"{control.Name} must remain accessible without scrolling.");
        Assert.That(control.Width, Is.GreaterThan(0), control.Name);
        Assert.That(control.Height, Is.GreaterThan(0), control.Name);
        Assert.That(control.GlobalPosition.X, Is.GreaterThanOrEqualTo(parent.GlobalPosition.X - 1), control.Name);
        Assert.That(control.GlobalPosition.Y, Is.GreaterThanOrEqualTo(parent.GlobalPosition.Y - 1), control.Name);
        Assert.That(control.GlobalPosition.X + control.Width, Is.LessThanOrEqualTo(parent.GlobalPosition.X + parent.Width + 1), control.Name);
        Assert.That(control.GlobalPosition.Y + control.Height, Is.LessThanOrEqualTo(parent.GlobalPosition.Y + parent.Height + 1), control.Name);
    }

    private static void AssertCaptionFits(Label label, IUserInterfaceManager ui, string? caption = null)
    {
        caption ??= label.Text!;
        var font = label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled)
            ? styled : ui.ThemeDefaults.LabelFont);
        var width = 0f;
        foreach (var rune in caption.EnumerateRunes())
            width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
        Assert.That(label.PixelWidth + 1, Is.GreaterThanOrEqualTo(width), $"Console captions must fit: {caption}");
        Assert.That(label.PixelHeight + 1, Is.GreaterThanOrEqualTo(font.GetHeight(label.UIScale)));
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var item in Descendants(child))
            yield return item;
    }
}
