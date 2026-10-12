using Content.Client._WF.ChatTabs;
using Content.Shared.Chat;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Robust.Client.UserInterface.Controls.LineEdit;

namespace Content.Client.UserInterface.Systems.Chat.Widgets;

// Chat tabs and the pop-out log; see Content.Client/_WF/ChatTabs.
public partial class ChatBox
{
    // Lines a tab switch puts back in the log. Laying a line out costs time, and the history is never trimmed.
    public const int WfRefillLimit = 1000;

    private ChatTabsUIController _wfTabs = default!;
    private ChatLogPopoutUIController _wfPopout = default!;
    private ChatTabBar? _wfTabBar;
    private Control? _wfPlaceholder;

    /// <summary>
    /// Whether the tab bar and the log are in the pop-out window, leaving the input line here.
    /// </summary>
    public bool WfLogPoppedOut { get; private set; }

    /// <summary>
    /// The tab bar above the log.
    /// </summary>
    public ChatTabBar WfTabBar => _wfTabBar!;

    protected override void EnteredTree()
    {
        base.EnteredTree();

        WfEnsureTabBar();

        // A chat box outside the tree gets no messages; it catches up from the history when it comes back.
        _controller.MessageAdded -= OnMessageAdded;
        _controller.MessageAdded += OnMessageAdded;
        _wfTabs.FilterChanged += WfApplyTab;
        WfApplyTab();

        _wfTabBar!.PopoutButton.Visible = _wfPopout.Enabled || WfLogPoppedOut;
        _wfPopout.ChatEntered(this);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _controller.MessageAdded -= OnMessageAdded;
        _wfTabs.FilterChanged -= WfApplyTab;
        _wfPopout.ChatExited(this);
    }

    private void WfEnsureTabBar()
    {
        if (_wfTabBar != null)
            return;

        _wfTabs = UserInterfaceManager.GetUIController<ChatTabsUIController>();
        _wfPopout = UserInterfaceManager.GetUIController<ChatLogPopoutUIController>();

        _wfTabBar = new ChatTabBar();
        _wfTabBar.PopoutButton.OnPressed += _ => _wfPopout.Toggle(this);
        ChatInput.Parent!.AddChild(_wfTabBar);
        _wfTabBar.SetPositionFirst();
        Contents.Margin = new Thickness(8, 2, 8, 4);

        // The filter popup edits the selected tab, which then refilters every chat box.
        ChatInput.FilterButton.Popup.OnChannelFilter -= OnChannelFilter;
        ChatInput.FilterButton.Popup.OnChannelFilter += WfOnChannelFilter;

        ChatInput.Input.OnFocusEnter += WfOnInputFocusEnter;
        ChatInput.Input.OnFocusExit += WfOnInputFocusExit;
    }

    /// <summary>
    /// Shows the selected tab's channels: the newest <see cref="WfRefillLimit"/> messages on them.
    /// </summary>
    private void WfApplyTab()
    {
        var channels = _wfTabs.SelectedTab.Channels;
        var filter = ChatInput.FilterButton.Popup;
        filter.WfSetActive(channels);

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
            if (shown++ < WfRefillLimit)
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

    private void WfOnChannelFilter(ChatChannel channel, bool active)
    {
        var channels = _wfTabs.SelectedTab.Channels;
        _wfTabs.SetChannels(_wfTabs.Selected, active ? channels | channel : channels & ~channel);
    }

    private void WfOnInputFocusEnter(LineEditEventArgs args)
    {
        if (WfLogPoppedOut)
            _wfPopout.SetTextInput(true);
    }

    private void WfOnInputFocusExit(LineEditEventArgs args)
    {
        if (WfLogPoppedOut)
            _wfPopout.SetTextInput(false);
    }

    /// <summary>
    /// Moves the tab bar and the log into the pop-out window.
    /// </summary>
    public void WfPopOutLog(Control host)
    {
        if (WfLogPoppedOut)
            return;

        _wfTabBar!.Orphan();
        Contents.Orphan();
        host.AddChild(_wfTabBar);
        host.AddChild(Contents);

        WfLogPoppedOut = true;
        _wfTabBar.PopoutButton.PoppedOut = true;
        _wfTabBar.PopoutButton.Visible = true;
        WfLogMoved(true);
    }

    /// <summary>
    /// Puts the tab bar and the log back above the input line.
    /// </summary>
    public void WfDockLog()
    {
        if (!WfLogPoppedOut)
            return;

        WfLogPoppedOut = false;
        _wfTabBar!.Orphan();
        Contents.Orphan();

        // Disposed while popped out: the rest of this chat box is already gone.
        if (ChatInput.Parent is not { Disposed: false } body)
            return;

        WfLogMoved(false);
        body.AddChild(_wfTabBar);
        body.AddChild(Contents);
        _wfTabBar.SetPositionFirst();
        Contents.SetPositionInParent(1);

        _wfTabBar.PopoutButton.PoppedOut = false;
        _wfTabBar.PopoutButton.Visible = _wfPopout.Enabled;
    }

    /// <summary>
    /// Lays the chat box out again after its log left or came back. Here a notice fills the gap.
    /// </summary>
    protected virtual void WfLogMoved(bool poppedOut)
    {
        if (!poppedOut)
        {
            _wfPlaceholder?.Orphan();
            return;
        }

        if (_wfPlaceholder == null)
        {
            var dock = new Button
            {
                Text = Loc.GetString("wf-chat-tabs-dock-button"),
                HorizontalAlignment = HAlignment.Center,
            };
            dock.OnPressed += _ => _wfPopout.Toggle(this);

            _wfPlaceholder = new BoxContainer
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
                        Text = Loc.GetString("wf-chat-tabs-popped-out"),
                        HorizontalAlignment = HAlignment.Center,
                    },
                    dock,
                },
            };
        }

        ChatInput.Parent!.AddChild(_wfPlaceholder);
        _wfPlaceholder.SetPositionFirst();
    }
}
