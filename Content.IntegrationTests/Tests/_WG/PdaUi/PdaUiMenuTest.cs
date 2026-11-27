#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Content.Client._WG.PdaUi;
using Content.Shared._WG.PdaUi;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.PdaUi;

/// <summary>
/// The PDA shell lists programs with their install state, shows the details and settings the
/// server sends, marks the running program and switches views without losing the program host.
/// </summary>
[TestFixture]
[TestOf(typeof(PdaOsMenu))]
public sealed class PdaUiMenuTest
{
    [Test]
    public async Task MainViewShowsProgramsAndStateTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
        });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var noteUid = new EntityUid(1);
            var newsUid = new EntityUid(2);
            var noteNet = new NetEntity(1);
            var newsNet = new NetEntity(2);
            var programs = new List<(EntityUid, CartridgeComponent)>
            {
                (noteUid, new CartridgeComponent { ProgramName = "notekeeper-program-name" }),
                (newsUid, new CartridgeComponent
                {
                    ProgramName = "news-read-program-name",
                    InstallationStatus = InstallationStatus.Installed,
                }),
            };

            var menu = new PdaOsMenu();
            menu.UpdateAvailablePrograms(programs);

            // Navigation labels must take layout space, not just be present as text.
            menu.HomeButton.Measure(new Vector2(200, 100));
            Assert.That(menu.HomeButton.DesiredSize.X, Is.GreaterThan(30),
                "the navigation label is laid out next to its icon");

            var list = ProgramList(menu);
            Assert.That(list.ChildCount, Is.EqualTo(2), "one row per program");

            var first = (PdaProgramRow) list.GetChild(0);
            Assert.That(first.ProgramUid, Is.EqualTo(noteUid));
            Assert.That(first.ProgramName, Is.EqualTo(Loc.GetString("notekeeper-program-name")));
            Assert.That(first.Action.Visible, Is.True, "a cartridge offers installation");
            Assert.That(first.Action.Text, Is.EqualTo(Loc.GetString("cartridge-bound-user-interface-install-button")));

            var second = (PdaProgramRow) list.GetChild(1);
            Assert.That(second.Action.Text, Is.EqualTo(Loc.GetString("cartridge-bound-user-interface-uninstall-button")));

            var pdaState = new PdaUpdateState(
                new List<NetEntity> { noteNet, newsNet },
                activeUI: null,
                flashlightEnabled: false,
                hasPen: false,
                hasPai: false,
                pdaOwnerInfo: new PdaIdInfoText
                {
                    ActualOwnerName = "Tester",
                    IdOwner = "Tester",
                    JobTitle = "Engineer",
                    StationAlertLevel = "green",
                    StationAlertColor = Color.Green,
                    WarLevel = "COLD",
                    CurrentDate = new DateTime(2026, 11, 21),
                },
                balance: 0,
                ownedShipName: null,
                stationName: "Test Station",
                address: "1234",
                notificationsEnabled: false,
                hasDisk: false);

            menu.UpdateState(pdaState);

            Assert.That(first.Status, Is.Null, "no program is marked before one is hosted");
            menu.SetActiveProgram(noteUid);

            Assert.That(first.Status, Is.EqualTo(Loc.GetString("pda-ui-status-running")),
                "the hosted program is marked as running");
            Assert.That(second.Status, Is.Null, "an idle program is not marked");
            Assert.That(menu.NotificationsButton.State, Is.EqualTo(Loc.GetString("pda-ui-toggle-off")));

            // The eject button follows the transmitted disk occupancy.
            Assert.That(menu.EjectDiskButton.IsActive, Is.False, "no disk inserted");
            pdaState.HasDisk = true;
            menu.UpdateState(pdaState);
            Assert.That(menu.EjectDiskButton.IsActive, Is.True, "disk inserted enables ejection");

            menu.ToProgramView("Notes");
            Assert.That(menu.ProgramView.Visible, Is.True);

            menu.ToSettings();
            Assert.That(menu.SettingsButton.IsCurrent, Is.True);
            Assert.That(menu.ProgramView.Visible, Is.False);

            menu.ToInfo();
            Assert.That(menu.InfoButton.IsCurrent, Is.True);
            Assert.That(menu.SettingsButton.IsCurrent, Is.False);
            Assert.That(menu.ProgramView.Visible, Is.False);

            menu.ToHomeScreen();
            Assert.That(menu.HomeButton.IsCurrent, Is.True);
            Assert.That(menu.ProgramView.Visible, Is.False);

            // A program title with an icon opens without touching the previous icon state.
            var icon = PdaProgramIcons.Get("notekeeper-program-name");
            Assert.That(icon, Is.Not.Null);
            menu.ToProgramView("Notekeeper", icon);

            // Stock cartridge controls pick up the PDA button and input classes.
            var fragment = new BoxContainer();
            var button = new Button { Text = "Ok" };
            var input = new LineEdit();
            fragment.AddChild(button);
            fragment.AddChild(input);
            PdaAppTheme.Apply(fragment);
            Assert.That(button.HasStyleClass(PdaAppTheme.ButtonClass), Is.True);
            Assert.That(input.HasStyleClass(PdaAppTheme.InputClass), Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NotificationToggleFlipsLoaderSettingTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var sEntMan = server.ResolveDependency<IEntityManager>();

        EntityUid pda = default;
        await server.WaitPost(() =>
        {
            pda = sEntMan.SpawnEntity("PassengerPDA", MapCoordinates.Nullspace);
        });

        await server.WaitAssertion(() =>
        {
            var loader = sEntMan.GetComponent<CartridgeLoaderComponent>(pda);
            Assert.That(loader.NotificationsEnabled, Is.True, "program notifications default on");

            sEntMan.EventBus.RaiseLocalEvent(pda,
                new PdaToggleNotificationsMessage { UiKey = PdaUiKey.Key });
            Assert.That(loader.NotificationsEnabled, Is.False);

            sEntMan.EventBus.RaiseLocalEvent(pda,
                new PdaToggleNotificationsMessage { UiKey = PdaUiKey.Key });
            Assert.That(loader.NotificationsEnabled, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProgramIconsLoadTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var cache = pair.Client.ResolveDependency<IResourceCache>();

        await pair.Client.WaitAssertion(() =>
        {
            var icons = (Dictionary<string, SpriteSpecifier>) typeof(PdaProgramIcons)
                .GetField("Icons", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;

            Assert.That(icons, Is.Not.Empty);

            var cached = new Dictionary<ResPath, RSI>();
            foreach (var (program, specifier) in icons)
            {
                var rsi = (SpriteSpecifier.Rsi) specifier;
                if (!cached.TryGetValue(rsi.RsiPath, out var sheet))
                {
                    sheet = cache.GetResource<RSIResource>(rsi.RsiPath).RSI;
                    cached[rsi.RsiPath] = sheet;
                }

                Assert.That(sheet.TryGetState(rsi.RsiState, out _), Is.True,
                    $"{program} points at missing state {rsi.RsiState}");
            }

            // Window chrome glyphs and the title mark must load too.
            foreach (var field in typeof(PdaIcons).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not SpriteSpecifier specifier)
                    continue;

                switch (specifier)
                {
                    case SpriteSpecifier.Rsi rsi:
                        Assert.That(cache.GetResource<RSIResource>(rsi.RsiPath).RSI
                                .TryGetState(rsi.RsiState, out _),
                            Is.True, $"{field.Name} points at missing state {rsi.RsiState}");
                        break;
                    case SpriteSpecifier.Texture texture:
                        Assert.That(cache.TryGetResource<TextureResource>(texture.TexturePath, out _),
                            Is.True, $"{field.Name} points at a missing texture");
                        break;
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    private static BoxContainer ProgramList(PdaOsMenu menu)
    {
        return (BoxContainer) typeof(PdaOsMenu)
            .GetProperty("ProgramList", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(menu)!;
    }
}
