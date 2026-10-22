#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.MappingMode;
using Content.Client._WF.MappingMode.Palette;
using Content.Client._WF.MappingMode.Screen;
using Content.Client._WF.UserInterface.WindowPopout;
using Content.Server._WF.MappingMode;
using Content.Shared._WF.MappingMode;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// An undocked mapping panel comes back whole however its window goes: closed in game, popped out and closed from
/// the OS window, its OS window torn down, or docked from the slot. Tile icons never wrap an atlas in an atlas,
/// which Clyde can't draw and which blanked the whole UI.
/// </summary>
[TestFixture]
public sealed class WFMappingDockPopoutTest
{
    private const string SessionName = "dock popout test";

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
    public async Task TileIconsAreDrawable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        var tileDefs = client.ResolveDependency<ITileDefinitionManager>();

        await client.WaitAssertion(() =>
        {
            var icons = new WFMappingPaletteIcons();
            var checkedRsi = false;
            Assert.Multiple(() =>
            {
                foreach (var tile in tileDefs)
                {
                    if (icons.Tile(tile.ID) is not AtlasTexture icon)
                        continue;

                    Assert.That(icon.SourceTexture, Is.Not.InstanceOf<AtlasTexture>(),
                        $"{tile.ID}'s icon wraps an atlas in an atlas.");

                    if (tile.ID != "FloorPlanetDirt")
                        continue;

                    // An RSI-backed tile with four variants; its RSI frame is already one cell.
                    checkedRsi = true;
                    Assert.That(icon.Size, Is.EqualTo(new Vector2i(32, 32)), "The dirt icon is one 32px cell.");
                }
            });
            Assert.That(checkedRsi, Is.True, "FloorPlanetDirt has an icon.");

            // Cutting a region from an atlas cuts it from the atlas's own source, at the right offset.
            var sheet = (AtlasTexture) icons.Tile("FloorPlanetDirt")!;
            var cut = WFMappingPaletteIcons.SubRegion(sheet, UIBox2.FromDimensions(0, 0, 16, 16));
            Assert.Multiple(() =>
            {
                Assert.That(cut.SourceTexture, Is.SameAs(sheet.SourceTexture));
                Assert.That(cut.SubRegion.TopLeft, Is.EqualTo(sheet.SubRegion.TopLeft));
                Assert.That(cut.Size, Is.EqualTo(new Vector2i(16, 16)));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DockPopout()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var client = pair.Client;
        var mapping = server.EntMan.System<WFMappingModeSystem>();
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        var uiMan = client.ResolveDependency<IUserInterfaceManager>();
        var popouts = uiMan.GetUIController<WindowPopoutUIController>();
        await Enable(pair);

        await client.WaitPost(() => clientSys.Send(new WFMappingCreateRequest(SessionName)));
        for (var i = 0; i < 100 && uiMan.ActiveScreen is not WFMappingScreen; i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(10);

        WFMappingDockSlot slot = default!;
        List<Control> panel = default!;
        await client.WaitAssertion(() =>
        {
            Assert.That(uiMan.ActiveScreen, Is.TypeOf<WFMappingScreen>());
            var screen = (WFMappingScreen) uiMan.ActiveScreen!;
            slot = Descendants(screen.PaletteHost).OfType<WFMappingDockSlot>().Single();
            panel = slot.Content.Children.ToList();
            Assert.That(panel, Is.Not.Empty, "The palette sits in its slot.");
        });

        WFMappingDockWindow Undock()
        {
            slot.Undock();
            Assert.That(slot.Undocked, Is.True);
            return uiMan.WindowRoot.Children.OfType<WFMappingDockWindow>().Single();
        }

        WolfgatePopoutWindow PopOut(WFMappingDockWindow window)
        {
            popouts.Toggle(window);
            Assert.That(popouts.IsPoppedOut(window), Is.True);
            var popout = (WolfgatePopoutWindow) window.Parent!;
            Assert.Multiple(() =>
            {
                Assert.That(popout.ClydeWindow, Is.Not.Null);
                Assert.That(window.Root, Is.Not.Null.And.Not.SameAs(uiMan.RootControl), "The window is in its own root.");
                Assert.That(panel.All(c => c.Root == window.Root), Is.True, "The panel went with it.");
            });
            return popout;
        }

        void AssertDocked(string when)
        {
            var screen = (WFMappingScreen) uiMan.ActiveScreen!;
            Assert.Multiple(() =>
            {
                Assert.That(slot.Undocked, Is.False, $"{when}: the slot is docked.");
                Assert.That(panel.All(c => c.Parent == slot.Content), Is.True, $"{when}: the panel is back in its slot.");
                Assert.That(uiMan.WindowRoot.Children.OfType<WFMappingDockWindow>(), Is.Empty,
                    $"{when}: no dock window is left open.");

                foreach (var control in Descendants(screen))
                {
                    Assert.That(control.Root, Is.SameAs(uiMan.RootControl), $"{when}: {control} is in the main root.");
                    Assert.That(control.Disposed, Is.False, $"{when}: {control} is not disposed.");
                    Assert.That(control.Parent?.Children.Contains(control), Is.True,
                        $"{when}: {control} is among its parent's children.");
                }
            });
        }

        async Task Settle()
        {
            await pair.RunTicksSync(5);
        }

        // Plain close of the in-game dock window.
        await client.WaitPost(() => Undock().Close());
        await Settle();
        await client.WaitAssertion(() => AssertDocked("After closing the dock window"));

        // Popped out, then closed from the OS window's close button.
        WFMappingDockWindow window = default!;
        await client.WaitPost(() =>
        {
            window = Undock();
            PopOut(window).Close();
        });
        await Settle();
        await client.WaitAssertion(() =>
        {
            AssertDocked("After closing the OS window");
            Assert.That(popouts.IsPoppedOut(window), Is.False);
        });

        // Popped out, then the OS window destroyed as the real client does it: its root first, then the window.
        await client.WaitPost(() =>
        {
            window = Undock();
            var clydeWindow = PopOut(window).ClydeWindow!;
            uiMan.DestroyWindowRoot(clydeWindow);
            clydeWindow.Dispose();
        });
        await Settle();
        await client.WaitAssertion(() =>
        {
            AssertDocked("After the OS window was destroyed");
            Assert.That(popouts.IsPoppedOut(window), Is.False);
        });

        // Only the OS window's root torn down, with no close event at all.
        IClydeWindow? orphaned = null;
        WolfgatePopoutWindow lostPopout = default!;
        await client.WaitPost(() =>
        {
            window = Undock();
            lostPopout = PopOut(window);
            orphaned = lostPopout.ClydeWindow!;
            uiMan.DestroyWindowRoot(orphaned);
        });
        await Settle();
        await client.WaitAssertion(() =>
        {
            AssertDocked("After the OS window's root was torn down");
            Assert.That(lostPopout.ClydeWindow, Is.Null, "The stranded OS window was closed too.");
            Assert.That(popouts.IsPoppedOut(window), Is.False);
        });

        // Popped out, then docked from the slot's own Dock button.
        await client.WaitPost(() =>
        {
            window = Undock();
            PopOut(window);
            slot.Dock();
            slot.Dock();
        });
        await Settle();
        await client.WaitAssertion(() =>
        {
            AssertDocked("After docking from the slot");
            Assert.That(popouts.IsPoppedOut(window), Is.False);
        });

        // Leaving with the panel popped out docks it before the screen goes.
        await client.WaitPost(() => PopOut(Undock()));
        await server.WaitPost(() =>
        {
            if (mapping.TryGetMembership(pair.Player!.UserId, out _))
                mapping.LeaveSession(pair.Player.UserId, kicked: false);
        });
        for (var i = 0; i < 100 && uiMan.ActiveScreen is WFMappingScreen; i++)
        {
            await pair.RunTicksSync(1);
        }

        await Settle();
        await client.WaitAssertion(() =>
        {
            Assert.That(uiMan.ActiveScreen, Is.Not.TypeOf<WFMappingScreen>());
            Assert.That(uiMan.WindowRoot.Children.OfType<WFMappingDockWindow>(), Is.Empty);
            Assert.That(panel.All(c => c.Parent == slot.Content), Is.True, "The panel went home before unloading.");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
