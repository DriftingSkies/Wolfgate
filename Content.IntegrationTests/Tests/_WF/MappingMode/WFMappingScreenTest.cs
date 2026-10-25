#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.MappingMode;
using Content.Client._WF.MappingMode.Modes;
using Content.Client._WF.MappingMode.Palette;
using Content.Client._WF.MappingMode.QuickSelect;
using Content.Client._WF.MappingMode.Screen;
using Content.Client._WF.MappingMode.Template;
using Content.Client._WF.MappingTools;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Content.Shared.CCVar;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// The real client gets the mapping screen in a session: its widgets, the mode keys, the quick bar and the template
/// panel; a layout change keeps it, the modes claim clicks as they should, the template settings go through the
/// data folder, the menu bar keeps only the mapping buttons and its Mapping and Quicksave buttons work, and leaving
/// the session puts the chosen layout, the normal menu bar and the normal keys back.
/// </summary>
[TestFixture]
public sealed class WFMappingScreenTest
{
    private const string SessionName = "screen test";

    /// <summary>A 1x1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// <summary>The menu bar's buttons that stay on the mapping screen for everyone.</summary>
    private static readonly string[] KeptButtons = { "EscapeButton", "GuidebookButton", "FeedbackButton", "AHelpButton" };

    /// <summary>The keys of the hidden buttons: character, emotes, crafting, actions, language and the sector map.</summary>
    private static readonly BoundKeyFunction[] GameplayFunctions =
    {
        ContentKeyFunctions.OpenCharacterMenu,
        ContentKeyFunctions.OpenEmotesMenu,
        ContentKeyFunctions.OpenCraftingMenu,
        ContentKeyFunctions.OpenActionsMenu,
        ContentKeyFunctions.OpenLanguageMenu,
        ContentKeyFunctions.OpenSectorMap,
    };

    private static List<string> Versions(IResourceManager res, NetUserId user, string map)
    {
        var folder = WFMappingLibraryNames.MapFolder(user, map);
        return res.UserData.IsDir(folder) ? res.UserData.DirectoryEntries(folder).Order().ToList() : new List<string>();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    [Test]
    public async Task MappingScreen()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var mapping = server.EntMan.System<WFMappingModeSystem>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        var modes = client.EntMan.System<WFMappingBuildModeSystem>();
        var template = client.EntMan.System<WFMappingTemplateSystem>();
        var uiMan = client.ResolveDependency<IUserInterfaceManager>();
        var inputMan = client.ResolveDependency<IInputManager>();
        var states = client.ResolveDependency<IStateManager>();
        var userData = client.ResolveDependency<IResourceManager>().UserData;
        var screenUi = uiMan.GetUIController<WFMappingScreenUIController>();
        var menu = uiMan.GetUIController<WFMappingMenuUIController>();
        var folder = new ResPath("/Mapping/Templates") / WFMappingTemplateSystem.FolderKey(SessionName);
        await Enable(pair);

        // A template saved for this session's name before it exists is restored when it opens.
        var saved = new WFMappingTemplateSettings
        {
            Position = new Vector2(3f, -4f),
            ScaleX = 2f,
            ScaleY = 2f,
            Rotation = 45f,
            Opacity = 0.8f,
            Layer = WFMappingTemplateLayer.Over,
        };
        await client.WaitPost(() =>
        {
            userData.CreateDir(folder);
            userData.WriteAllBytes(folder / "template.png", Png);
            userData.WriteAllText(folder / "settings.yml", saved.Write());
        });

        await client.WaitPost(() => clientSys.Send(new WFMappingCreateRequest(SessionName)));
        for (var i = 0; i < 100 && uiMan.ActiveScreen is not WFMappingScreen; i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(10);

        void AssertMappingScreen(string when)
        {
            Assert.That(states.CurrentState, Is.TypeOf<GameplayState>(), when);
            Assert.That(uiMan.ActiveScreen, Is.TypeOf<WFMappingScreen>(), when);
            var screen = (WFMappingScreen) uiMan.ActiveScreen!;
            Assert.Multiple(() =>
            {
                Assert.That(clientSys.SetUp, Is.True, when);
                Assert.That(modes.Active, Is.True, when);
                foreach (var function in WFMappingKeyFunctions.Modes.Append(WFMappingKeyFunctions.WFMappingModePrimary))
                {
                    Assert.That(inputMan.Contexts.ActiveContext.FunctionExistsHierarchy(function), Is.True,
                        $"{when}: {function.FunctionName} is in the active context.");
                }

                Assert.That(screen.GetWidget<ChatBox>(), Is.Not.Null, when);
                Assert.That(screen.GetWidget<MainViewport>(), Is.Not.Null, when);
                Assert.That(screen.GetWidget<GameTopMenuBar>(), Is.Not.Null, when);
                Assert.That(screen.QuickBarHost.Children.OfType<WFMappingQuickBar>().Count(), Is.EqualTo(1),
                    $"{when}: one quick bar.");
                Assert.That(Descendants(screen.TemplateHost).OfType<WFMappingTemplatePanel>().Count(), Is.EqualTo(1),
                    $"{when}: one template panel.");
                Assert.That(Descendants(screen.PaletteHost).Any(), Is.True, $"{when}: the palette is docked.");

                // The menu bar: the gameplay buttons hidden, the mapping ones shown, Mapping, Quicksave, Test map and Export added.
                var bar = screen.GetWidget<GameTopMenuBar>()!;
                foreach (var name in WFMappingScreenUIController.GameplayButtonNames)
                {
                    Assert.That(bar.FindControl<MenuButton>(name).Visible, Is.False, $"{when}: {name} is hidden.");
                }

                foreach (var name in KeptButtons)
                {
                    Assert.That(bar.FindControl<MenuButton>(name).Visible, Is.True, $"{when}: {name} stays.");
                }

                Assert.That(bar.FindControl<MenuButton>("AdminButton").Visible, Is.False, $"{when}: a mapper is no admin.");
                Assert.That(bar.FindControl<MenuButton>("SandboxButton").Visible, Is.False, $"{when}: no sandbox.");
                var mappingButton = screenUi.MappingButton;
                var quicksaveButton = screenUi.QuicksaveButton;
                var testButton = screenUi.TestButton;
                var exportButton = screenUi.ExportButton;
                Assert.That(mappingButton?.Parent, Is.SameAs(bar), $"{when}: the Mapping button is on the bar.");
                Assert.That(quicksaveButton?.Parent, Is.SameAs(bar), $"{when}: the Quicksave button is on the bar.");
                Assert.That(testButton?.Parent, Is.SameAs(bar), $"{when}: the Test map button is on the bar.");
                Assert.That(exportButton?.Parent, Is.SameAs(bar), $"{when}: the Export button is on the bar.");
                Assert.That(mappingButton?.Visible, Is.True, when);
                Assert.That(quicksaveButton?.Visible, Is.True, when);
                Assert.That(testButton?.Visible, Is.True, when);
                Assert.That(exportButton?.Visible, Is.True, when);
                Assert.That(exportButton?.Disabled, Is.False, when);
                Assert.That(bar.Children.Count(c => c.Name is "WFMappingButton" or "WFQuicksaveButton" or "WFTestButton" or "WFExportButton"),
                    Is.EqualTo(4), $"{when}: one of each.");
                var feedback = bar.FindControl<MenuButton>("FeedbackButton").GetPositionInParent();
                Assert.That(mappingButton?.GetPositionInParent(), Is.EqualTo(feedback - 4), $"{when}: Mapping first.");
                Assert.That(quicksaveButton?.GetPositionInParent(), Is.EqualTo(feedback - 3), $"{when}: Quicksave next.");
                Assert.That(testButton?.GetPositionInParent(), Is.EqualTo(feedback - 2), $"{when}: Test map next.");
                Assert.That(exportButton?.GetPositionInParent(), Is.EqualTo(feedback - 1),
                    $"{when}: Export next, before the bug report button.");

                foreach (var function in GameplayFunctions)
                {
                    Assert.That(inputMan.Contexts.ActiveContext.FunctionExistsHierarchy(function), Is.False,
                        $"{when}: {function.FunctionName} can't be pressed.");
                }

                Assert.That(inputMan.Contexts.ActiveContext.FunctionExistsHierarchy(
                    WFMappingKeyFunctions.WFMappingQuicksave), Is.True, $"{when}: the quicksave key is in.");
            });
        }

        async Task Click(Control control)
        {
            var coords = new ScreenCoordinates(control.GlobalPixelPosition + control.PixelSize / 2,
                control.Window?.Id ?? default);
            var relative = coords.Position / control.UIScale - control.GlobalPosition;
            var relativePixel = coords.Position - control.GlobalPixelPosition;
            await client.DoGuiEvent(control, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down,
                coords, default, relative, relativePixel));
            await pair.RunTicksSync(1);
            await client.DoGuiEvent(control, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up,
                coords, default, relative, relativePixel));
            await pair.RunTicksSync(1);
        }

        await client.WaitAssertion(() =>
        {
            AssertMappingScreen("After joining");
            Assert.Multiple(() =>
            {
                Assert.That(template.HasTemplate, Is.True, "The saved template is restored.");
                Assert.That(template.Settings.Write(), Is.EqualTo(saved.Write()));
            });
        });

        // The Mapping button opens the Mapping window and shows pressed; a second press closes it.
        await Click(screenUi.MappingButton!);
        await client.WaitAssertion(() =>
        {
            Assert.That(menu.IsOpen, Is.True, "The Mapping button opens the Mapping window.");
            Assert.That(screenUi.MappingButton!.Pressed, Is.True, "and shows pressed.");
        });
        await Click(screenUi.MappingButton!);
        await client.WaitAssertion(() =>
        {
            Assert.That(menu.IsOpen, Is.False, "A second press closes it.");
            Assert.That(screenUi.MappingButton!.Pressed, Is.False);
        });

        // Quicksave writes a manual version under the session name. A second press inside the cooldown, by button
        // or key, sends nothing: the server would answer it with save-too-soon.
        var quicksave = screenUi.QuicksaveButton!;
        await Click(quicksave);
        await client.WaitAssertion(() => Assert.That(quicksave.Disabled, Is.True, "Quicksave cools down."));
        await Click(quicksave);
        await client.WaitAssertion(() =>
        {
            Assert.That(screenUi.Quicksave(), Is.False, "The key is blocked during the cooldown too.");
            var inputSys = client.EntMan.System<InputSystem>();
            var timing = client.ResolveDependency<IGameTiming>();
            var function = WFMappingKeyFunctions.WFMappingQuicksave;
            var message = new ClientFullInputCmdMessage(timing.CurTick, timing.TickFraction,
                inputMan.NetworkBindMap.KeyFunctionID(function))
            {
                State = BoundKeyState.Down,
            };
            Assert.That(inputSys.HandleInputCommand(client.Session!, function, message), Is.True,
                "The quicksave key is bound.");
        });

        var serverRes = server.ResolveDependency<IResourceManager>();
        var userId = pair.Player!.UserId;
        for (var i = 0; i < 100 && clientSys.LastResult?.LocKey != "wf-mapping-mode-saved"; i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            var versions = Versions(serverRes, userId, SessionName);
            Assert.That(versions, Has.Count.EqualTo(1), "One quicksave reached the library.");
            Assert.That(WFMappingLibraryNames.TryParseVersion(versions[0], out var version), Is.True, versions[0]);
            Assert.That(version.Number, Is.EqualTo(1));
            Assert.That(version.Kind, Is.EqualTo(WFMappingVersionKind.Manual));
            Assert.That(versions[0], Does.StartWith("v0001_").And.EndWith("_manual"));
        });
        await client.WaitAssertion(() =>
        {
            Assert.That(clientSys.LastResult?.LocKey, Is.EqualTo("wf-mapping-mode-saved"),
                "The server never heard the second press.");
            Assert.That(quicksave.Disabled, Is.True, "Still cooling down.");
        });

        // Changing ui.layout reloads the screen, which stays the mapping screen with every panel rebuilt once.
        var layout = string.Empty;
        await client.WaitPost(() =>
        {
            layout = client.CfgMan.GetCVar(CCVars.UILayout);
            client.CfgMan.SetCVar(CCVars.UILayout, nameof(ScreenType.Separated));
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() => AssertMappingScreen("After a layout change"));

        // Undocking a column's last docked panel collapses that column; docking one brings it back.
        await client.WaitAssertion(() =>
        {
            var screen = (WFMappingScreen) uiMan.ActiveScreen!;
            var palette = Descendants(screen.PaletteHost).OfType<WFMappingDockSlot>().Single();
            var tabs = Descendants(screen.RightTabs).OfType<WFMappingDockSlot>().ToList();
            Assert.Multiple(() =>
            {
                Assert.That(screen.LeftCollapsed, Is.False);
                Assert.That(screen.RightCollapsed, Is.False);
                Assert.That(tabs, Has.Count.GreaterThan(1));
            });

            palette.Undock();
            Assert.That(screen.LeftCollapsed, Is.True, "Undocking the palette collapses the left dock.");
            palette.Dock();
            Assert.That(screen.LeftCollapsed, Is.False, "Docking it brings the column back.");

            tabs[0].Undock();
            Assert.That(screen.RightCollapsed, Is.False, "The other tabs are still docked.");
            foreach (var tab in tabs)
            {
                tab.Undock();
            }

            Assert.Multiple(() =>
            {
                Assert.That(screen.RightCollapsed, Is.True, "Every tab undocked collapses the right dock.");
                Assert.That(Descendants(screen).Contains(screen.ChatBox), Is.True, "The chat stays on screen.");
                Assert.That(screen.GetWidget<GameTopMenuBar>()?.VisibleInTree, Is.True, "The top bar stays.");
                Assert.That(screen.QuickBarHost.VisibleInTree, Is.True, "The quick bar stays.");
            });

            tabs[1].Dock();
            Assert.That(screen.RightCollapsed, Is.False, "Docking one tab brings the column back.");
            foreach (var tab in tabs)
            {
                tab.Dock();
            }

            Assert.That(screen.RightCollapsed, Is.False);
        });

        // Select mode: a click on a tile with nothing on it selects that cell.
        NetEntity tileGridNet = default;
        await server.WaitPost(() =>
        {
            var serverMap = server.EntMan.System<SharedMapSystem>();
            var mapId = server.EntMan.GetComponent<TransformComponent>(pair.Player!.AttachedEntity!.Value).MapID;
            var tileGrid = serverMap.CreateGridEntity(mapId);
            server.EntMan.System<SharedTransformSystem>().SetWorldPosition(tileGrid.Owner, new Vector2(40f, 40f));
            serverMap.SetTile(tileGrid.Owner, tileGrid.Comp, new Vector2i(2, 3),
                new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId));
            tileGridNet = server.EntMan.GetNetEntity(tileGrid.Owner);
        });
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var tools = client.EntMan.System<MappingToolsSystem>();
            var tileGrid = client.EntMan.GetEntity(tileGridNet);
            Assert.That(client.EntMan.EntityExists(tileGrid), Is.True, "The tile grid reached the client.");

            modes.SetMode(WFMappingBuildMode.Select);
            Assert.That(modes.Mode, Is.EqualTo(WFMappingBuildMode.Select));
            tools.Selection.Clear();
            var at = new EntityCoordinates(tileGrid, new Vector2(2.5f, 3.5f));
            Select(BoundKeyState.Down, at);
            Select(BoundKeyState.Up, at);
            Assert.Multiple(() =>
            {
                Assert.That(tools.Selection.Grid, Is.EqualTo(tileGrid), "The tile's grid is selected.");
                Assert.That(tools.Selection.Area, Is.EqualTo(new Box2i(new Vector2i(2, 3), new Vector2i(3, 4))),
                    "The selection is that one cell.");
            });

            tools.Selection.Clear();
            modes.SetMode(WFMappingBuildMode.Place);
        });

        // Erase claims the left button; Place leaves it to the engine's placement.
        bool Select(BoundKeyState state, EntityCoordinates at)
        {
            var inputSys = client.EntMan.System<InputSystem>();
            var timing = client.ResolveDependency<IGameTiming>();
            var function = ContentKeyFunctions.WFMappingSelect;
            var message = new ClientFullInputCmdMessage(timing.CurTick, timing.TickFraction,
                inputMan.NetworkBindMap.KeyFunctionID(function))
            {
                State = state,
                Coordinates = at,
            };
            return inputSys.HandleInputCommand(client.Session!, function, message);
        }

        bool Press(BoundKeyState state)
        {
            var inputSys = client.EntMan.System<InputSystem>();
            var timing = client.ResolveDependency<IGameTiming>();
            var function = WFMappingKeyFunctions.WFMappingModePrimary;
            var message = new ClientFullInputCmdMessage(timing.CurTick, timing.TickFraction,
                inputMan.NetworkBindMap.KeyFunctionID(function))
            {
                State = state,
            };
            return inputSys.HandleInputCommand(client.Session!, function, message);
        }

        await client.WaitAssertion(() =>
        {
            modes.SetMode(WFMappingBuildMode.Erase);
            Assert.That(modes.Mode, Is.EqualTo(WFMappingBuildMode.Erase));
            Assert.Multiple(() =>
            {
                Assert.That(Press(BoundKeyState.Down), Is.True, "Erase claims the press.");
                Assert.That(Press(BoundKeyState.Up), Is.True, "and its release.");
            });

            modes.SetMode(WFMappingBuildMode.Place);
            Assert.That(modes.Mode, Is.EqualTo(WFMappingBuildMode.Place));
            Assert.Multiple(() =>
            {
                Assert.That(Press(BoundKeyState.Down), Is.False, "Place leaves the press to the placement manager.");
                Assert.That(Press(BoundKeyState.Up), Is.False);
            });

            // Template mode takes the wheel only with a template loaded; Place only with Ctrl.
            modes.SetMode(WFMappingBuildMode.Template);
            var scale = template.Settings.ScaleX;
            Assert.That(modes.TryWheel(1f), Is.True, "Template mode scales on the plain wheel.");
            Assert.That(template.Settings.ScaleX, Is.GreaterThan(scale));
            modes.SetMode(WFMappingBuildMode.Place);
            Assert.That(modes.TryWheel(1f), Is.False, "Place mode leaves the plain wheel to the zoom.");

            // Picking another palette entry keeps the mode; only Pick hands over to Place.
            Assert.That(client.ResolveDependency<Robust.Client.Placement.IPlacementManager>().Replacement, Is.True,
                "Placement replaces like walls by default.");
            modes.SetMode(WFMappingBuildMode.Fill);
            modes.BeginPlacing(new WFMappingPaletteEntry(WFMappingPaletteKind.Tile, "Plating"));
            Assert.That(modes.Mode, Is.EqualTo(WFMappingBuildMode.Fill));
            modes.SetMode(WFMappingBuildMode.Pick);
            modes.BeginPlacing(new WFMappingPaletteEntry(WFMappingPaletteKind.Tile, "Plating"));
            Assert.That(modes.Mode, Is.EqualTo(WFMappingBuildMode.Place));

            // A changed setting is written to the data folder when the session is left.
            template.Settings.Opacity = 0.25f;
            template.Modified();
        });

        // Leaving: the player is put on a plain entity, as when an admin ghosts out of the session.
        EntityUid elsewhere = default;
        await server.WaitPost(() =>
        {
            var map = server.EntMan.System<SharedMapSystem>().CreateMap(out var mapId);
            elsewhere = server.EntMan.SpawnEntity(null, new MapCoordinates(Vector2.Zero, mapId));
            server.ResolveDependency<Robust.Server.Player.IPlayerManager>().SetAttachedEntity(pair.Player!, elsewhere, force: true);
            Assert.That(map.IsValid(), Is.True);
        });

        for (var i = 0; i < 100 && uiMan.ActiveScreen is WFMappingScreen; i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            Assert.That(states.CurrentState, Is.TypeOf<GameplayState>());
            Assert.Multiple(() =>
            {
                Assert.That(clientSys.SetUp, Is.False);
                Assert.That(modes.Active, Is.False);
                Assert.That(uiMan.ActiveScreen, Is.TypeOf<SeparatedChatGameScreen>(), "The layout ui.layout names.");
                foreach (var function in WFMappingKeyFunctions.Modes.Append(WFMappingKeyFunctions.WFMappingModePrimary))
                {
                    Assert.That(inputMan.Contexts.ActiveContext.FunctionExistsHierarchy(function), Is.False,
                        $"{function.FunctionName} is gone.");
                }

                Assert.That(template.HasTemplate, Is.False, "The template is dropped with the session.");
                Assert.That(userData.TryReadAllText(folder / "settings.yml", out var text), Is.True);
                Assert.That(WFMappingTemplateSettings.Read(text!).Opacity, Is.EqualTo(0.25f), "Saved on leaving.");
                Assert.That(userData.Exists(folder / "template.png"), Is.True, "Leaving keeps the image.");

                // The normal menu bar is untouched: its gameplay buttons show, the mapping ones were never on it.
                var bar = uiMan.ActiveScreen!.GetWidget<GameTopMenuBar>()!;
                foreach (var name in WFMappingScreenUIController.GameplayButtonNames)
                {
                    // Crafting follows what the entity can build; consent and anatomy share one slot.
                    if (name is "CraftingButton" or "ConsentButton" or "AnatomyButton")
                        continue;

                    Assert.That(bar.FindControl<MenuButton>(name).Visible, Is.True, $"{name} is back.");
                }

                Assert.That(bar.FindControl<MenuButton>("ConsentButton").Visible,
                    Is.Not.EqualTo(bar.FindControl<MenuButton>("AnatomyButton").Visible), "One of consent or anatomy.");
                Assert.That(bar.Children.Any(c => c.Name is "WFMappingButton" or "WFQuicksaveButton" or "WFTestButton" or "WFExportButton"), Is.False,
                    "No Mapping, Quicksave, Test map or Export button on the normal bar.");
                Assert.That(screenUi.MappingButton, Is.Null);
                Assert.That(screenUi.QuicksaveButton, Is.Null);
                Assert.That(screenUi.TestButton, Is.Null);
                Assert.That(screenUi.ExportButton, Is.Null);
                Assert.That(menu.IsOpen, Is.False);

                var common = inputMan.Contexts.GetContext("common");
                foreach (var function in WFMappingScreenUIController.GameplayKeys)
                {
                    Assert.That(common.FunctionExists(function), Is.True, $"{function.FunctionName} is back.");
                }

                Assert.That(common.FunctionExists(WFMappingKeyFunctions.WFMappingQuicksave), Is.False,
                    "The quicksave key is gone.");
            });

            client.CfgMan.SetCVar(CCVars.UILayout, layout);
        });

        await server.WaitPost(() =>
        {
            if (mapping.TryGetMembership(pair.Player!.UserId, out _))
                mapping.LeaveSession(pair.Player.UserId, kicked: false);
            server.EntMan.DeleteEntity(elsewhere);
        });
        await pair.RunTicksSync(5);

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
