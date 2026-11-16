using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ShipAdmin.Tabs;

/// <summary>Who may board: the lock, the mode and the allow list. Per-door rules and codes stay on the ship's own console.</summary>
public sealed class WFShipAdminAccessTab : WFShipAdminTab
{
    private readonly WFShipAdminText _status;
    private readonly WFShipAdminText _noKey;
    private readonly Button _lock;
    private readonly WFShipAdminDropdown _mode;
    private readonly Button _setMode;
    private readonly BoxContainer _allowed;
    private readonly PlayerListControl _players;
    private readonly Button _add;
    private readonly ConfirmButton _clear;
    private readonly ConfirmButton _resetDoors;
    private readonly CheckBox _notify;

    private PlayerInfo? _player;
    private AdminFlags _flags;
    private bool _locked;

    public override string Title => Loc.GetString("wf-ship-admin-tab-access");

    public WFShipAdminAccessTab()
    {
        _status = Status();
        Body.AddChild(_status);
        _noKey = Note(Loc.GetString("wf-ship-admin-access-no-key"));
        Body.AddChild(_noKey);

        _lock = new Button { StyleClasses = { "ButtonSquare" } };
        _lock.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.SetLocked, !_locked, _notify!.Pressed));

        _mode = Dropdown(150);
        foreach (var mode in Enum.GetValues<WFShipAccessMode>())
            _mode.AddItem(WFShipAdminOverviewTab.Mode(mode), (int) mode);

        _setMode = new Button
        {
            Text = Loc.GetString("wf-ship-admin-access-set-mode"),
            ToolTip = Loc.GetString("wf-ship-admin-access-set-mode-tooltip"),
            StyleClasses = { "ButtonSquare" },
        };
        _setMode.OnPressed += _ => Send(new WFShipAdminModeMessage(Grid, (WFShipAccessMode) _mode.SelectedId, _notify!.Pressed));

        var controls = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        controls.AddChild(_lock);
        controls.AddChild(_mode);
        controls.AddChild(_setMode);
        Body.AddChild(controls);

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-overview-allowed")));
        _allowed = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        Body.AddChild(_allowed);

        Body.AddChild(Heading(Loc.GetString("wf-ship-admin-access-add-heading")));
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-access-add-note")));
        _players = new PlayerListControl { MinSize = new Vector2(0, 150) };
        _players.OnSelectionChanged += player =>
        {
            _player = player;
            UpdateButtons();
        };
        Body.AddChild(_players);

        _add = new Button { Text = Loc.GetString("wf-ship-admin-access-add"), StyleClasses = { "ButtonSquare" } };
        _add.OnPressed += _ =>
        {
            if (_player != null)
                Send(new WFShipAdminAllowMessage(Grid, _player.SessionId, null, _notify!.Pressed));
        };
        _clear = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-access-clear"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _clear.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.AllowClear, notify: _notify!.Pressed));
        _resetDoors = new ConfirmButton
        {
            Text = Loc.GetString("wf-ship-admin-access-reset-doors"),
            ToolTip = Loc.GetString("wf-ship-admin-access-reset-doors-tooltip"),
            ConfirmationText = Loc.GetString("wf-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _resetDoors.OnPressed += _ => Send(new WFShipAdminActionMessage(Grid, WFShipAdminAction.ResetDoors, notify: _notify!.Pressed));

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        buttons.AddChild(_add);
        buttons.AddChild(_clear);
        buttons.AddChild(_resetDoors);
        Body.AddChild(buttons);

        _notify = new CheckBox
        {
            Text = Loc.GetString("wf-ship-admin-notify"),
            ToolTip = Loc.GetString("wf-ship-admin-notify-tooltip"),
            Pressed = true,
        };
        Body.AddChild(_notify);
        Body.AddChild(Note(Loc.GetString("wf-ship-admin-access-console-note")));
    }

    /// <summary>How many allow list entries the tab shows, for tests.</summary>
    public int AllowedCount { get; private set; }

    protected override void Show(WFShipAdminDetail detail, AdminFlags flags)
    {
        _flags = flags;
        _locked = detail.Locked;
        AllowedCount = detail.AllowList.Count;

        _status.Text = !detail.HasAccess
            ? Loc.GetString("wf-ship-admin-overview-no-access")
            : Loc.GetString("wf-ship-admin-access-status",
                ("mode", WFShipAdminOverviewTab.Mode(detail.Mode)),
                ("locked", detail.Locked ? "true" : "false"));
        _noKey.Visible = detail.HasAccess && !detail.CanLock;

        _lock.Text = Loc.GetString(detail.Locked ? "wf-ship-admin-access-unlock" : "wf-ship-admin-access-lock");
        Gate(_lock, WFShipAdminAction.SetLocked, flags, detail.Locked || detail.CanLock || !detail.HasAccess);
        Gate(_setMode, WFShipAdminAction.SetMode, flags);
        Gate(_clear, WFShipAdminAction.AllowClear, flags, detail.AllowList.Count > 0);
        Gate(_resetDoors, WFShipAdminAction.ResetDoors, flags);

        _allowed.RemoveAllChildren();
        if (detail.AllowList.Count == 0)
            _allowed.AddChild(Note(Loc.GetString("wf-ship-admin-access-nobody")));

        foreach (var entry in detail.AllowList)
            _allowed.AddChild(AllowedRow(entry, flags));

        UpdateButtons();
    }

    private Control AllowedRow(WFShipAccessEntry entry, AdminFlags flags)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        row.AddChild(Line(entry.Name, entry.Label, false));

        var remove = new Button { Text = Loc.GetString("wf-ship-admin-ownership-remove"), StyleClasses = { "ButtonSquare" } };
        Gate(remove, WFShipAdminAction.AllowRemove, flags);
        var key = entry.Key;
        remove.OnPressed += _ => Send(new WFShipAdminAllowMessage(Grid, null, key, _notify.Pressed));
        row.AddChild(remove);
        return row;
    }

    private void UpdateButtons()
    {
        Gate(_add, WFShipAdminAction.AllowAdd, _flags, _player != null);
    }
}
