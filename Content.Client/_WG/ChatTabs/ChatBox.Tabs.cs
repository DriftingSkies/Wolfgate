using Content.Client._WG.ChatTabs;
using Content.Client.UserInterface.Systems.Chat;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.LineEdit;

namespace Content.Client.UserInterface.Systems.Chat.Widgets;

// Chat tabs and the pop-out log; see Content.Client/_WG/ChatTabs.
public partial class ChatBox
{
    // Lines a tab switch puts back in the log. Laying a line out costs time, and the history is never trimmed.
    public const int WgRefillLimit = 1000;

    private ChatTabsUIController _wgTabs = default!;
    private ChatLogPopoutUIController _wgPopout = default!;
    private ChatTabBar? _wgTabBar;
    private Control? _wgPlaceholder;

    /// <summary>
    /// Whether the tab bar and the log are in the pop-out window, leaving the input line here.
    /// </summary>
    public bool WgLogPoppedOut { get; private set; }

    /// <summary>
    /// The tab bar above the log.
    /// </summary>
    public ChatTabBar WgTabBar => _wgTabBar!;

    protected override void EnteredTree()
    {
        base.EnteredTree();

        // A throw here stops the engine's tree entry before the children: the input line then never starts text
        // input, and the chat is dead until the player rejoins. Nothing below is worth that.
        try
        {
            WgEnsureTabBar();

            // A chat box outside the tree gets no messages; it catches up from the history when it comes back.
            _controller.MessageAdded -= OnMessageAdded;
            _controller.MessageAdded += OnMessageAdded;
            _wgTabs.FilterChanged += WgApplyTab;
            WgApplyTab();

            _wgTabBar!.PopoutButton.Visible = _wgPopout.Enabled || WgLogPoppedOut;
            _wgPopout.ChatEntered(this);
        }
        catch (Exception e)
        {
            Logger.GetSawmill("wg.chat").Error($"Chat box failed to set up on entering the tree: {e}");
        }
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _controller.MessageAdded -= OnMessageAdded;
        _wgTabs.FilterChanged -= WgApplyTab;
        _wgPopout.ChatExited(this);
    }

    private void WgEnsureTabBar()
    {
        if (_wgTabBar != null)
            return;

        _wgTabs = UserInterfaceManager.GetUIController<ChatTabsUIController>();
        _wgPopout = UserInterfaceManager.GetUIController<ChatLogPopoutUIController>();

        _wgTabBar = new ChatTabBar();
        _wgTabBar.PopoutButton.OnPressed += _ => _wgPopout.Toggle(this);
        _wgTabBar.SaveButton.OnPressed += _ => SaveChatLog();
        ChatInput.Parent!.AddChild(_wgTabBar);
        _wgTabBar.SetPositionFirst();
        Contents.Margin = new Thickness(8, 2, 8, 4);

        // The filter popup edits the selected tab, which then refilters every chat box.
        ChatInput.FilterButton.Popup.OnChannelFilter -= OnChannelFilter;
        ChatInput.FilterButton.Popup.OnChannelFilter += WgOnChannelFilter;

        ChatInput.Input.OnFocusEnter += WgOnInputFocusEnter;
        ChatInput.Input.OnFocusExit += WgOnInputFocusExit;
    }

    /// <summary>
    /// Shows the selected tab's channels: the newest <see cref="WgRefillLimit"/> messages on them.
    /// </summary>
    private void WgApplyTab()
    {
        var channels = _wgTabs.SelectedTab.Channels;
        var filter = ChatInput.FilterButton.Popup;
        filter.WgSetActive(channels);

        var history = _controller.History;
        var start = history.Count;
        var shown = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var msg = history[i].Msg;
            if (!filter.IsActive(msg.Channel))
                continue;

            // Sound cues play when a message arrives, not when a tab first shows it.
            msg.Read = true;
            if (shown++ < WgRefillLimit)
                start = i;
        }

        Contents.Clear();
        _lastLine = null;
        _lastLineRepeatCount = 0;
        for (var i = start; i < history.Count; i++)
        {
            OnMessageAdded(history[i].Msg);
        }

        _controller.ClearUnfilteredUnreads(channels);
    }

    /// <summary>
    /// Adds a chat line. One that does not parse stays in the history for the round, so it must not throw: every
    /// later refill would stop at it, and a refill runs as the chat box enters the tree.
    /// </summary>
    private static void WgAddMarkup(FormattedMessage formatted, string message)
    {
        try
        {
            formatted.AddMarkupOrThrow(message);
        }
        catch (Exception)
        {
            // The permissive parser keeps the tags that do parse; it can still give up.
            try
            {
                formatted.AddMarkupPermissive(message);
            }
            catch (Exception)
            {
                formatted.AddText(message);
            }
        }
    }

    /// <summary>
    /// Adds the entry to the log. A tag that throws while laying out (bad parameters, say) would otherwise throw out of
    /// every refill for the rest of the round, so the line falls back to its text.
    /// </summary>
    private void WgAddEntry(FormattedMessage formatted, string message, Color color)
    {
        try
        {
            Contents.AddMessage(formatted, tagsAllowed: null);
        }
        catch (Exception e)
        {
            Logger.GetSawmill("wg.chat").Error($"Chat line could not be laid out, showing it as text: {e}");
            var plain = new FormattedMessage(3);
            plain.PushColor(color);
            plain.AddText(message);
            plain.Pop();
            Contents.AddMessage(plain, tagsAllowed: null);
        }
    }

    private void WgOnChannelFilter(ChatChannel channel, bool active)
    {
        var channels = _wgTabs.SelectedTab.Channels;
        _wgTabs.SetChannels(_wgTabs.Selected, active ? channels | channel : channels & ~channel);
    }

    private void WgOnInputFocusEnter(LineEditEventArgs args)
    {
        if (WgLogPoppedOut)
            _wgPopout.SetTextInput(true);
    }

    private void WgOnInputFocusExit(LineEditEventArgs args)
    {
        if (WgLogPoppedOut)
            _wgPopout.SetTextInput(false);
    }

    /// <summary>
    /// Moves the tab bar and the log into the pop-out window.
    /// </summary>
    public void WgPopOutLog(Control host)
    {
        if (WgLogPoppedOut)
            return;

        _wgTabBar!.Orphan();
        Contents.Orphan();
        host.AddChild(_wgTabBar);
        host.AddChild(Contents);

        WgLogPoppedOut = true;
        _wgTabBar.PopoutButton.PoppedOut = true;
        _wgTabBar.PopoutButton.Visible = true;
        WgLogMoved(true);
    }

    /// <summary>
    /// Puts the tab bar and the log back above the input line.
    /// </summary>
    public void WgDockLog()
    {
        if (!WgLogPoppedOut)
            return;

        WgLogPoppedOut = false;
        _wgTabBar!.Orphan();
        Contents.Orphan();

        // Disposed while popped out: the rest of this chat box is already gone.
        if (ChatInput.Parent is not { Disposed: false } body)
            return;

        WgLogMoved(false);
        body.AddChild(_wgTabBar);
        body.AddChild(Contents);
        _wgTabBar.SetPositionFirst();
        Contents.SetPositionInParent(1);

        _wgTabBar.PopoutButton.PoppedOut = false;
        _wgTabBar.PopoutButton.Visible = _wgPopout.Enabled;
    }

    /// <summary>
    /// Lays the chat box out again after its log left or came back. Here a notice fills the gap.
    /// </summary>
    protected virtual void WgLogMoved(bool poppedOut)
    {
        if (!poppedOut)
        {
            _wgPlaceholder?.Orphan();
            return;
        }

        if (_wgPlaceholder == null)
        {
            var dock = new Button
            {
                Text = Loc.GetString("wg-chat-tabs-dock-button"),
                HorizontalAlignment = HAlignment.Center,
            };
            dock.OnPressed += _ => _wgPopout.Toggle(this);

            _wgPlaceholder = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                VerticalExpand = true,
                VerticalAlignment = VAlignment.Center,
                SeparationOverride = 6,
                Margin = new Thickness(8),
                Children =
                {
                    new Label
                    {
                        Text = Loc.GetString("wg-chat-tabs-popped-out"),
                        HorizontalAlignment = HAlignment.Center,
                    },
                    dock,
                },
            };
        }

        ChatInput.Parent!.AddChild(_wgPlaceholder);
        _wgPlaceholder.SetPositionFirst();
    }

    private async void SaveChatLog()
    {
        var fileDialog = IoCManager.Resolve<IFileDialogManager>();
        var filters = new FileDialogFilters(new FileDialogFilters.Group("html"));
        var file = await fileDialog.SaveFile(filters);
        if (file == null)
            return;

        var controller = UserInterfaceManager.GetUIController<ChatUIController>();
        var tab = _wgTabs.SelectedTab;
        try
        {
            await ChatLogSaver.SaveChatLogAsync(file.Value.fileStream, controller, tab);
        }
        catch (Exception e)
        {
            Logger.GetSawmill("wg.chat").Error($"Failed to save client chat log: {e}");
        }
    }
}
