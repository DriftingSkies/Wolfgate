#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.ChatTabs;
using Content.Client.UserInterface.Controls;
using Content.Shared.CCVar;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.CCVar;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.ChatTabs;

/// <summary>
/// Chat tabs filter the in-game chat box, are edited and saved, and the log pops out into its own window and back.
/// </summary>
[TestFixture]
[TestOf(typeof(ChatTabsUIController))]
public sealed class ChatTabsTest : InteractionTest
{
    // The default tabs.
    private const int All = 0;
    private const int Local = 1;
    private const int Radio = 2;
    private const int Ooc = 3;

    private IUserInterfaceManager _ui = default!;
    private IConfigurationManager _cfg = default!;
    private ChatUIController _chat = default!;
    private ChatTabsUIController _tabs = default!;
    private ChatLogPopoutUIController _popout = default!;
    private ChatBox _box = default!;
    private OutputPanel _log = default!;

    private async Task Prepare()
    {
        _ui = Client.ResolveDependency<IUserInterfaceManager>();
        _cfg = Client.ResolveDependency<IConfigurationManager>();

        await Client.WaitPost(() =>
        {
            _chat = _ui.GetUIController<ChatUIController>();
            _tabs = _ui.GetUIController<ChatTabsUIController>();
            _popout = _ui.GetUIController<ChatLogPopoutUIController>();
            _box = ((InGameScreen) _ui.ActiveScreen!).ChatBox;
            _log = _box.FindControl<OutputPanel>("Contents");

            _chat.History.Clear();
            _tabs.Load(string.Empty);
        });
    }

    [TearDown]
    public async Task ChatTabsTearDown()
    {
        await Client.WaitPost(() =>
        {
            if (_box.WfLogPoppedOut)
                _popout.Toggle(_box);

            _chat.History.Clear();
            _cfg.SetCVar(WolfgateCVars.ChatTabs, string.Empty);
            _tabs.Load(string.Empty);
        });
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;

            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private void Say(ChatChannel channel, string text)
    {
        _chat.ProcessChatMessage(new ChatMessage(channel, text, text, NetEntity.Invalid, null), speechBubble: false);
    }

    [Test]
    public async Task TabsFilterTheLog()
    {
        await Prepare();

        await Client.WaitAssertion(() =>
        {
            Assert.That(_tabs.Tabs, Has.Count.EqualTo(4));
            Assert.That(_box.WfTabBar.IsInsideTree);
            Assert.That(_log.EntryCount, Is.Zero);

            Say(ChatChannel.OOC, "ooc one");
            Say(ChatChannel.Local, "local one");
            Say(ChatChannel.Radio, "radio one");
            Assert.That(_log.EntryCount, Is.EqualTo(3), "the All tab shows every channel");

            _tabs.Select(Ooc);
            Assert.That(_log.EntryCount, Is.EqualTo(1), "the OOC tab shows OOC only");

            _tabs.Select(Local);
            Assert.That(_log.EntryCount, Is.EqualTo(1));

            // A message the selected tab hides is counted on the tabs that show it.
            Say(ChatChannel.OOC, "ooc two");
            Assert.That(_log.EntryCount, Is.EqualTo(1));
            Assert.That(_tabs.Unread(_tabs.Tabs[Ooc]), Is.EqualTo(1));
            Assert.That(_tabs.Unread(_tabs.Tabs[All]), Is.EqualTo(1));
            Assert.That(_tabs.Unread(_tabs.Tabs[Radio]), Is.Zero);
            Assert.That(_tabs.Unread(_tabs.Tabs[Local]), Is.Zero);

            // A message the selected tab shows isn't unread anywhere.
            Say(ChatChannel.Local, "local two");
            Assert.That(_log.EntryCount, Is.EqualTo(2));
            Assert.That(_tabs.Unread(_tabs.Tabs[All]), Is.EqualTo(1));

            // Reading it on one tab clears it from every tab that shows it.
            _tabs.Select(Ooc);
            Assert.That(_log.EntryCount, Is.EqualTo(2));
            Assert.That(_tabs.Unread(_tabs.Tabs[Ooc]), Is.Zero);
            Assert.That(_tabs.Unread(_tabs.Tabs[All]), Is.Zero);

            _tabs.Select(All);
            Assert.That(_log.EntryCount, Is.EqualTo(5));
            Assert.That(_tabs.Unread(_tabs.Tabs[All]), Is.Zero);
        });
    }

    /// <summary>The filter button's checkboxes show the selected tab and edit it.</summary>
    [Test]
    public async Task FilterPopupEditsTheSelectedTab()
    {
        await Prepare();
        await RunTicks(3);

        var popup = _box.ChatInput.FilterButton.Popup;
        await Client.WaitAssertion(() =>
        {
            Say(ChatChannel.OOC, "ooc");
            Say(ChatChannel.Local, "local");

            _tabs.Select(Local);
            Assert.That(popup.IsActive(ChatChannel.Local));
            Assert.That(popup.IsActive(ChatChannel.OOC), Is.False);
            Assert.That(_log.EntryCount, Is.EqualTo(1));
        });

        await ClickControl(_box.ChatInput.FilterButton);
        await RunTicks(3);
        Assert.That(popup.Visible);

        var checkbox = Descendants(popup).OfType<ChannelFilterCheckbox>().Single(c => c.Channel == ChatChannel.OOC);
        await ClickControl(checkbox);

        await Client.WaitAssertion(() =>
        {
            Assert.That(_tabs.Tabs[Local].Channels & ChatChannel.OOC, Is.EqualTo(ChatChannel.OOC));
            Assert.That(popup.IsActive(ChatChannel.OOC));
            Assert.That(_log.EntryCount, Is.EqualTo(2));
            Assert.That(_cfg.GetCVar(WolfgateCVars.ChatTabs), Does.Contain("Local=Local,Whisper,Emotes,OOC|"));
            popup.Close();
        });
    }

    /// <summary>The tab row: a click selects, a right-click opens the tab's settings, + adds a tab.</summary>
    [Test]
    public async Task TabRowIsClickable()
    {
        await Prepare();
        await RunTicks(3);

        Button TabButton(string name) => Descendants(_box.WfTabBar).OfType<Button>().Single(b => b.Text == name);
        ChatTabPopup Settings() => _ui.ModalRoot.Children.OfType<ChatTabPopup>().Single();
        var loc = Client.ResolveDependency<ILocalizationManager>();
        Button SettingsButton(string id) =>
            Descendants(Settings()).OfType<Button>().Single(b => b.Text == loc.GetString(id));

        await ClickControl(TabButton("OOC"));
        Assert.That(_tabs.Selected, Is.EqualTo(Ooc));
        Assert.That(TabButton("OOC").Pressed);
        Assert.That(TabButton("All").Pressed, Is.False);

        // Clicking the selected tab again leaves it selected.
        await ClickControl(TabButton("OOC"));
        Assert.That(TabButton("OOC").Pressed);

        await ClickControl(TabButton("Radio"), EngineKeyFunctions.UIRightClick);
        await RunTicks(3);
        Assert.That(Settings().Visible);
        Assert.That(_tabs.Selected, Is.EqualTo(Ooc), "editing a tab doesn't select it");

        await ClickControl(Descendants(Settings()).OfType<CheckBox>()
            .Single(c => c.Text == loc.GetString("hud-chatbox-channel-OOC")));
        Assert.That(_tabs.Tabs[Radio].Channels, Is.EqualTo(ChatChannel.Radio | ChatChannel.OOC));

        await ClickControl(SettingsButton("wf-chat-tabs-settings-none"));
        Assert.That(_tabs.Tabs[Radio].Channels, Is.EqualTo(ChatChannel.None));

        await ClickControl(SettingsButton("wf-chat-tabs-settings-delete"));
        Assert.That(_tabs.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "All", "Local", "OOC" }));
        Assert.That(_tabs.SelectedTab.Name, Is.EqualTo("OOC"), "the selection follows its tab");
        Assert.That(Settings().Visible, Is.False);

        await ClickControl(TabButton("+"));
        await RunTicks(3);
        Assert.That(_tabs.Tabs, Has.Count.EqualTo(4));
        Assert.That(_tabs.Selected, Is.EqualTo(3));
        Assert.That(Settings().Visible, "a new tab opens its settings");
        await Client.WaitPost(() => Settings().Close());
    }

    /// <summary>A row with more tabs than fit scrolls to keep the selected tab in view.</summary>
    [Test]
    public async Task TabRowScrollsToTheSelectedTab()
    {
        await Prepare();
        await Client.WaitPost(() =>
        {
            while (_tabs.Add() != null)
            {
            }
        });
        await RunTicks(5);

        var scroll = Descendants(_box.WfTabBar).OfType<ScrollContainer>().Single();
        var buttons = scroll.Children.OfType<BoxContainer>().Single();

        void AssertSelectedInView()
        {
            var button = buttons.GetChild(_tabs.Selected);
            Assert.That(button.Position.X, Is.GreaterThanOrEqualTo(scroll.HScrollTarget - 0.5f));
            Assert.That(button.Position.X + button.Size.X,
                Is.LessThanOrEqualTo(scroll.HScrollTarget + scroll.Size.X + 0.5f));
        }

        await Client.WaitAssertion(() =>
        {
            Assert.That(buttons.Size.X, Is.GreaterThan(scroll.Size.X), "the tabs don't fit");
            Assert.That(_tabs.Selected, Is.EqualTo(ChatTab.MaxTabs - 1));
            Assert.That(scroll.HScrollTarget, Is.GreaterThan(0));
            AssertSelectedInView();
        });

        await Client.WaitPost(() => _tabs.Select(All));
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(scroll.HScrollTarget, Is.Zero);
            AssertSelectedInView();
        });
    }

    /// <summary>A tab switch refills the log with the newest lines only, so a long round doesn't stall it.</summary>
    [Test]
    public async Task TabSwitchRefillsTheNewestLines()
    {
        await Prepare();

        await Client.WaitAssertion(() =>
        {
            const int perChannel = ChatBox.WfRefillLimit + 50;
            for (var i = 0; i < perChannel; i++)
            {
                Say(ChatChannel.OOC, $"ooc {i}");
                Say(ChatChannel.Local, $"local {i}");
            }

            Assert.That(_log.EntryCount, Is.EqualTo(perChannel * 2), "arriving lines are never dropped");

            foreach (var tab in new[] { Ooc, All, Local })
            {
                _tabs.Select(tab);
                Assert.That(_log.EntryCount, Is.EqualTo(ChatBox.WfRefillLimit));
            }

            Say(ChatChannel.Local, "one more");
            Assert.That(_log.EntryCount, Is.EqualTo(ChatBox.WfRefillLimit + 1));
        });
    }

    [Test]
    public async Task TabsAreEditedAndSaved()
    {
        await Prepare();

        await Client.WaitAssertion(() =>
        {
            var added = _tabs.Add();
            Assert.That(added, Is.EqualTo(4));
            Assert.That(_tabs.Selected, Is.EqualTo(4), "a new tab is selected");
            Assert.That(_tabs.Tabs[4].Channels, Is.EqualTo(ChatChannel.None));

            _tabs.Rename(4, "  Dead|chat=  ");
            _tabs.SetChannels(4, ChatChannel.Dead);
            Assert.That(_tabs.Tabs[4].Name, Is.EqualTo("Deadchat"));

            Assert.That(_tabs.Move(4, -1), Is.EqualTo(3));
            Assert.That(_tabs.Selected, Is.EqualTo(3), "the selection follows a moved tab");
            Assert.That(_tabs.Move(3, 5), Is.EqualTo(3), "a move off the end does nothing");

            var saved = _cfg.GetCVar(WolfgateCVars.ChatTabs);
            Assert.That(saved, Does.EndWith("|Deadchat=Dead|OOC=LOOC,OOC"));

            // Deleting the selected tab selects its neighbour.
            _tabs.Remove(3);
            Assert.That(_tabs.Tabs, Has.Count.EqualTo(4));
            Assert.That(_tabs.Selected, Is.EqualTo(3));
            Assert.That(_tabs.SelectedTab.Channels, Is.EqualTo(ChatChannel.OOC | ChatChannel.LOOC));

            // The saved value brings the same tabs back.
            saved = _cfg.GetCVar(WolfgateCVars.ChatTabs);
            _tabs.Load(saved);
            Assert.That(_tabs.Tabs.Select(t => t.Name), Is.EqualTo(new[] { "All", "Local", "Radio", "OOC" }));
            Assert.That(_tabs.Tabs[All].Channels, Is.EqualTo(ChatTab.AllChannels));

            // The last tab can't be deleted, and the row is capped.
            _tabs.Remove(0);
            _tabs.Remove(0);
            _tabs.Remove(0);
            _tabs.Remove(0);
            Assert.That(_tabs.Tabs, Has.Count.EqualTo(1));

            for (var i = 0; i < ChatTab.MaxTabs + 3; i++)
            {
                _tabs.Add();
            }

            Assert.That(_tabs.Tabs, Has.Count.EqualTo(ChatTab.MaxTabs));
        });
    }

    [Test]
    public async Task LogPopsOutAndDocks()
    {
        await Prepare();
        await RunTicks(3);

        var body = _box.ChatInput.Parent!;
        var minSize = _box.MinSize;
        var marginBottom = _box.GetValue<float>(LayoutContainer.MarginBottomProperty);

        await Client.WaitAssertion(() =>
        {
            Assert.That(_box.WfTabBar.PopoutButton.Visible);
            Say(ChatChannel.OOC, "before");
        });

        await ClickControl(_box.WfTabBar.PopoutButton);

        await Client.WaitAssertion(() =>
        {
            Assert.That(_box.WfLogPoppedOut);
            var window = _popout.Window;
            Assert.That(window, Is.Not.Null);
            Assert.That(_box.WfTabBar.Parent, Is.EqualTo(window!.Body));
            Assert.That(_log.Parent, Is.EqualTo(window.Body));
            Assert.That(_box.ChatInput.Parent, Is.EqualTo(body), "the input line stays in the game window");

            // The popped-out log still fills and still follows the tabs.
            Say(ChatChannel.OOC, "after");
            Assert.That(_log.EntryCount, Is.EqualTo(2));
            _tabs.Select(Radio);
            Assert.That(_log.EntryCount, Is.Zero);
            _tabs.Select(All);
        });

        await RunTicks(3);

        await Client.WaitAssertion(() =>
        {
            if (_box is ResizableChatBox)
            {
                Assert.That(_box.MinSize.Y, Is.Zero);
                Assert.That(_box.Size.Y, Is.LessThan(80), "the floating chat box shrinks to its input line");
                Assert.That(_box.Size.Y, Is.GreaterThan(10));
            }
            else
            {
                Assert.That(body.ChildCount, Is.EqualTo(2), "a docked chat box shows a notice above the input line");
            }
        });

        await ClickControl(_box.WfTabBar.PopoutButton);

        await Client.WaitAssertion(() =>
        {
            Assert.That(_box.WfLogPoppedOut, Is.False);
            Assert.That(_popout.Window, Is.Null);
            Assert.That(body.Children.ToArray(), Is.EqualTo(new Control[] { _box.WfTabBar, _log, _box.ChatInput }));
            Assert.That(_box.MinSize, Is.EqualTo(minSize));
            Assert.That(_box.GetValue<float>(LayoutContainer.MarginBottomProperty), Is.EqualTo(marginBottom));
            Assert.That(_log.EntryCount, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// A change of screen takes the chat box out of the tree and puts another in. The log window stays open for
    /// it, and closes if none comes.
    /// </summary>
    [Test]
    public async Task LogWindowOutlivesAScreenChange()
    {
        await Prepare();
        await RunTicks(3);

        var parent = _box.Parent!;
        var position = _box.GetPositionInParent();
        ChatLogWindow? window = null;

        try
        {
            await Client.WaitAssertion(() =>
            {
                _popout.Toggle(_box);
                window = _popout.Window;

                parent.RemoveChild(_box);
                Assert.That(_box.WfLogPoppedOut, Is.False, "a chat box leaving the tree takes its log with it");
                Assert.That(_log.Parent, Is.EqualTo(_box.ChatInput.Parent));
                Assert.That(_popout.Window, Is.EqualTo(window), "the window waits for the next chat box");

                parent.AddChild(_box);
                _box.SetPositionInParent(position);
            });

            await RunTicks(3);

            await Client.WaitAssertion(() =>
            {
                Assert.That(_box.WfLogPoppedOut, "the log goes back out once the chat box is back");
                Assert.That(_popout.Window, Is.EqualTo(window));
                Assert.That(_log.Parent, Is.EqualTo(window!.Body));

                parent.RemoveChild(_box);
            });

            await RunTicks(20);

            await Client.WaitAssertion(() =>
            {
                Assert.That(_popout.Window, Is.Null, "with no chat box the window closes");
                Assert.That(window!.Body.ChildCount, Is.Zero);

                parent.AddChild(_box);
                _box.SetPositionInParent(position);
            });

            await RunTicks(3);

            await Client.WaitAssertion(() =>
            {
                Assert.That(_box.WfLogPoppedOut, "the log pops out again in a new window");
                Assert.That(_popout.Window, Is.Not.Null);
            });
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                if (_box.Parent == null)
                {
                    parent.AddChild(_box);
                    _box.SetPositionInParent(position);
                }
            });
        }
    }

    /// <summary>
    /// A line whose markup does not parse stays in the history, so it must not throw: the refill a chat box runs as it
    /// enters the tree would stop there and leave the input line outside the tree, where it takes no text.
    /// </summary>
    [Test]
    public async Task ALineWithBrokenMarkupDoesNotBreakTheChatBox()
    {
        await Prepare();

        ChatBox? plain = null;
        try
        {
            await Client.WaitAssertion(() =>
            {
                Say(ChatChannel.Radio, "before");
                Assert.DoesNotThrow(() => Say(ChatChannel.Radio, "plain [bracket"));
                Assert.DoesNotThrow(() => Say(ChatChannel.Radio, "[color=red]red [bracket[/color]"));
                Say(ChatChannel.Radio, "after");
                Assert.That(_log.EntryCount, Is.EqualTo(4), "the broken lines are shown like any other");
                Assert.That(_log.GetMessage(1).ToString(), Does.Contain("plain [bracket"));
                Assert.That(_log.GetMessage(2).ToString(), Does.Contain("bracket"));

                Assert.DoesNotThrow(() => _tabs.Select(Radio), "a tab switch refills past the broken lines");
                Assert.That(_log.EntryCount, Is.EqualTo(4));

                plain = new ChatBox { SetSize = new Vector2(400, 300) };
                Assert.DoesNotThrow(() => _ui.WindowRoot.AddChild(plain));
                Assert.That(plain.FindControl<OutputPanel>("Contents").EntryCount, Is.EqualTo(4));
                Assert.That(plain.ChatInput.Input.IsInsideTree, "the input line must enter the tree to take text");
            });
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                if (plain == null)
                    return;

                plain.Orphan();
                _chat.UnregisterChat(plain);
            });
        }
    }

    /// <summary>
    /// The game screen is added to the tree before its controllers set it up: the world view gets its size after that. A chat box
    /// that threw while entering the tree left a black view with the HUD collapsed in the middle of the window.
    /// </summary>
    [Test]
    public async Task ALineWithBrokenMarkupDoesNotBreakLoadingTheGameScreen()
    {
        await Prepare();

        var layout = _cfg.GetCVar(CCVars.UILayout);
        try
        {
            await Client.WaitAssertion(() =>
            {
                Say(ChatChannel.OOC, "plain [bracket");

                foreach (var next in new[] { "Separated", "Default" })
                {
                    _cfg.SetCVar(CCVars.UILayout, next);

                    var screen = (InGameScreen) _ui.ActiveScreen!;
                    Assert.That(screen.GetType().Name, Does.StartWith(next), "the layout change loaded its screen");
                    Assert.That(screen.HorizontalAlignment, Is.EqualTo(Control.HAlignment.Stretch), $"{next}: the screen fills the window");
                    Assert.That(screen.VerticalAlignment, Is.EqualTo(Control.VAlignment.Stretch));
                    Assert.That(Descendants(screen).Where(control => !control.IsInsideTree), Is.Empty,
                        $"{next}: every HUD control entered the tree");
                    var view = screen.GetWidget<MainViewport>()!.Viewport;
                    Assert.That(Client.ResolveDependency<Robust.Client.Graphics.IEyeManager>().MainViewport, Is.SameAs(view),
                        $"{next}: the screen finished loading, so the world view is the one drawn to");
                    Assert.That(view.ViewportSize.X * view.ViewportSize.Y, Is.GreaterThan(0), $"{next}: the world view has a size");
                    Assert.That(screen.ChatBox.ChatInput.Input.IsInsideTree, $"{next}: the chat input takes text");
                    Assert.That(screen.ChatBox.FindControl<OutputPanel>("Contents").EntryCount, Is.EqualTo(1));
                }
            });
        }
        finally
        {
            await Client.WaitPost(() => _cfg.SetCVar(CCVars.UILayout, layout));
        }
    }

    /// <summary>A chat box that isn't the floating one (lobby, separated layout) keeps its size and shows a notice.</summary>
    [Test]
    public async Task DockedChatBoxShowsANotice()
    {
        await Prepare();

        ChatBox? plain = null;
        try
        {
            await Client.WaitAssertion(() =>
            {
                Say(ChatChannel.OOC, "earlier");

                plain = new ChatBox { SetSize = new Vector2(400, 300) };
                _ui.WindowRoot.AddChild(plain);

                var log = plain.FindControl<OutputPanel>("Contents");
                var body = plain.ChatInput.Parent!;
                Assert.That(log.EntryCount, Is.EqualTo(1), "a chat box entering the tree fills from the history");
                Assert.That(body.ChildCount, Is.EqualTo(3));

                _popout.Toggle(plain);
                Assert.That(plain.WfLogPoppedOut);
                Assert.That(body.ChildCount, Is.EqualTo(2));
                Assert.That(body.GetChild(1), Is.EqualTo(plain.ChatInput));
                Assert.That(plain.MinSize.Y, Is.GreaterThan(0));

                // Popping out another chat box docks the first.
                _popout.Toggle(_box);
                Assert.That(plain.WfLogPoppedOut, Is.False);
                Assert.That(_box.WfLogPoppedOut);
                Assert.That(body.Children.ToArray(), Is.EqualTo(new Control[] { plain.WfTabBar, log, plain.ChatInput }));
            });
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                if (plain == null)
                    return;

                plain.Orphan();
                _chat.UnregisterChat(plain);
            });
        }
    }
}
