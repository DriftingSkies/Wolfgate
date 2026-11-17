using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipAdmin.Tabs;

/// <summary>Who may board: the lock, the mode and the allow list. Per-door rules and codes stay on the ship's own console.</summary>
public sealed class WGShipAdminAccessTab : WGShipAdminTab
{
    private readonly WGShipAdminText _status;
    private readonly WGShipAdminText _noKey;
    private readonly Button _lock;
    private readonly WGShipAdminDropdown _mode;
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

    public override string Title => Loc.GetString("wg-ship-admin-tab-access");

    public WGShipAdminAccessTab()
    {
        _status = Status();
        Body.AddChild(_status);
        _noKey = Note(Loc.GetString("wg-ship-admin-access-no-key"));
        Body.AddChild(_noKey);

        _lock = new Button { StyleClasses = { "ButtonSquare" } };
        _lock.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.SetLocked, !_locked, _notify!.Pressed));

        _mode = Dropdown(150);
        foreach (var mode in Enum.GetValues<WGShipAccessMode>())
            _mode.AddItem(WGShipAdminOverviewTab.Mode(mode), (int) mode);

        _setMode = new Button
        {
            Text = Loc.GetString("wg-ship-admin-access-set-mode"),
            ToolTip = Loc.GetString("wg-ship-admin-access-set-mode-tooltip"),
            StyleClasses = { "ButtonSquare" },
        };
        _setMode.OnPressed += _ => Send(new WGShipAdminModeMessage(Grid, (WGShipAccessMode) _mode.SelectedId, _notify!.Pressed));

        var controls = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        controls.AddChild(_lock);
        controls.AddChild(_mode);
        controls.AddChild(_setMode);
        Body.AddChild(controls);

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-overview-allowed")));
        _allowed = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        Body.AddChild(_allowed);

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-access-add-heading")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-access-add-note")));
        _players = new PlayerListControl { MinSize = new Vector2(0, 150) };
        _players.OnSelectionChanged += player =>
        {
            _player = player;
            UpdateButtons();
        };
        Body.AddChild(_players);

        _add = new Button { Text = Loc.GetString("wg-ship-admin-access-add"), StyleClasses = { "ButtonSquare" } };
        _add.OnPressed += _ =>
        {
            if (_player != null)
                Send(new WGShipAdminAllowMessage(Grid, _player.SessionId, null, _notify!.Pressed));
        };
        _clear = new ConfirmButton
        {
            Text = Loc.GetString("wg-ship-admin-access-clear"),
            ConfirmationText = Loc.GetString("wg-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _clear.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.AllowClear, notify: _notify!.Pressed));
        _resetDoors = new ConfirmButton
        {
            Text = Loc.GetString("wg-ship-admin-access-reset-doors"),
            ToolTip = Loc.GetString("wg-ship-admin-access-reset-doors-tooltip"),
            ConfirmationText = Loc.GetString("wg-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
        _resetDoors.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.ResetDoors, notify: _notify!.Pressed));

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 4 };
        buttons.AddChild(_add);
        buttons.AddChild(_clear);
        buttons.AddChild(_resetDoors);
        Body.AddChild(buttons);

        _notify = new CheckBox
        {
            Text = Loc.GetString("wg-ship-admin-notify"),
            ToolTip = Loc.GetString("wg-ship-admin-notify-tooltip"),
            Pressed = true,
        };
        Body.AddChild(_notify);
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-access-console-note")));
    }

    /// <summary>How many allow list entries the tab shows, for tests.</summary>
    public int AllowedCount { get; private set; }

    protected override void Show(WGShipAdminDetail detail, AdminFlags flags)
    {
        _flags = flags;
        _locked = detail.Locked;
        AllowedCount = detail.AllowList.Count;

        _status.Text = !detail.HasAccess
            ? Loc.GetString("wg-ship-admin-overview-no-access")
            : Loc.GetString("wg-ship-admin-access-status",
                ("mode", WGShipAdminOverviewTab.Mode(detail.Mode)),
                ("locked", detail.Locked ? "true" : "false"));
        _noKey.Visible = detail.HasAccess && !detail.CanLock;

        _lock.Text = Loc.GetString(detail.Locked ? "wg-ship-admin-access-unlock" : "wg-ship-admin-access-lock");
        Gate(_lock, WGShipAdminAction.SetLocked, flags, detail.Locked || detail.CanLock || !detail.HasAccess);
        Gate(_setMode, WGShipAdminAction.SetMode, flags);
        Gate(_clear, WGShipAdminAction.AllowClear, flags, detail.AllowList.Count > 0);
        Gate(_resetDoors, WGShipAdminAction.ResetDoors, flags);

        _allowed.RemoveAllChildren();
        if (detail.AllowList.Count == 0)
            _allowed.AddChild(Note(Loc.GetString("wg-ship-admin-access-nobody")));

        foreach (var entry in detail.AllowList)
            _allowed.AddChild(AllowedRow(entry, flags));

        UpdateButtons();
    }

    private Control AllowedRow(WGShipAccessEntry entry, AdminFlags flags)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        row.AddChild(Line(entry.Name, entry.Label, false));

        var remove = new Button { Text = Loc.GetString("wg-ship-admin-ownership-remove"), StyleClasses = { "ButtonSquare" } };
        Gate(remove, WGShipAdminAction.AllowRemove, flags);
        var key = entry.Key;
        remove.OnPressed += _ => Send(new WGShipAdminAllowMessage(Grid, null, key, _notify.Pressed));
        row.AddChild(remove);
        return row;
    }

    private void UpdateButtons()
    {
        Gate(_add, WGShipAdminAction.AllowAdd, _flags, _player != null);
    }
}
