using System.Globalization;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipAdmin.Tabs;

/// <summary>Rescuing a ship: bring it here, send it somewhere, dock it, stop it, and free its helm and jump drive.</summary>
public sealed class WGShipAdminMoveTab : WGShipAdminTab
{
    private readonly WGShipAdminText _status;
    private readonly ConfirmButton _toMe;
    private readonly LineEdit _x;
    private readonly LineEdit _y;
    private readonly ConfirmButton _toPosition;
    private readonly WGShipAdminDropdown _targets;
    private readonly ConfirmButton _nextTo;
    private readonly ConfirmButton _dockAt;
    private readonly Button _undock;
    private readonly Button _halt;
    private readonly Button _clearPilots;
    private readonly Button _ftlLock;
    private readonly Button _resetFtl;

    private readonly List<NetEntity> _targetGrids = new();
    private readonly List<string> _targetNames = new();
    private bool _ftlLocked;

    public override string Title => Loc.GetString("wg-ship-admin-tab-move");

    public WGShipAdminMoveTab()
    {
        _status = Status();
        Body.AddChild(_status);

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-move-heading")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-move-note")));

        _toMe = Confirm("wg-ship-admin-move-to-me");
        _toMe.OnPressed += _ => SendMove(WGShipAdminMoveKind.ToMe, null, Vector2.Zero);
        Body.AddChild(Left(_toMe));

        _x = new LineEdit { PlaceHolder = Loc.GetString("wg-ship-admin-move-x"), MinWidth = 90 };
        _y = new LineEdit { PlaceHolder = Loc.GetString("wg-ship-admin-move-y"), MinWidth = 90 };
        _toPosition = Confirm("wg-ship-admin-move-to-position");
        _toPosition.OnPressed += _ =>
        {
            if (TryPosition(out var position))
                SendMove(WGShipAdminMoveKind.ToPosition, null, position);
        };
        _x.OnTextChanged += args => _toPosition.Disabled = !TryPosition(out var typed);
        _y.OnTextChanged += args => _toPosition.Disabled = !TryPosition(out var typed);
        Body.AddChild(Row(_x, _y, _toPosition));

        _targets = Dropdown();
        _nextTo = Confirm("wg-ship-admin-move-next-to");
        _nextTo.OnPressed += _ => SendMove(WGShipAdminMoveKind.NextTo, Target(), Vector2.Zero);
        _dockAt = Confirm("wg-ship-admin-move-dock-at");
        _dockAt.OnPressed += _ => SendMove(WGShipAdminMoveKind.DockAt, Target(), Vector2.Zero);
        Body.AddChild(Row(_targets, _nextTo, _dockAt));

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-move-free-heading")));
        _undock = Plain("wg-ship-admin-move-undock", WGShipAdminAction.Undock);
        _halt = Plain("wg-ship-admin-move-halt", WGShipAdminAction.Halt);
        _clearPilots = Plain("wg-ship-admin-move-clear-pilots", WGShipAdminAction.ClearPilots);
        _resetFtl = Plain("wg-ship-admin-move-reset-ftl", WGShipAdminAction.ResetFtl);
        _ftlLock = new Button { StyleClasses = { "ButtonSquare" }, ToolTip = Loc.GetString("wg-ship-admin-move-ftl-lock-tooltip") };
        _ftlLock.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.FtlLock, !_ftlLocked));
        Body.AddChild(Row(_undock, _halt, _clearPilots));
        Body.AddChild(Row(_resetFtl, _ftlLock));
    }

    public override void SetShips(List<WGShipAdminRow> ships)
    {
        // The list is sent again every few seconds; an open dropdown is only rebuilt when a ship came, went or was renamed.
        if (SameTargets(ships))
            return;

        var selected = Target();
        _targets.Clear();
        _targetGrids.Clear();
        _targetNames.Clear();
        foreach (var ship in ships)
        {
            _targets.AddItem(ship.Name, _targetGrids.Count);
            if (ship.Grid == selected)
                _targets.SelectId(_targetGrids.Count);

            _targetGrids.Add(ship.Grid);
            _targetNames.Add(ship.Name);
        }
    }

    private bool SameTargets(List<WGShipAdminRow> ships)
    {
        if (ships.Count != _targetGrids.Count)
            return false;

        for (var i = 0; i < ships.Count; i++)
        {
            if (ships[i].Grid != _targetGrids[i] || ships[i].Name != _targetNames[i])
                return false;
        }

        return true;
    }

    protected override void Show(WGShipAdminDetail detail, AdminFlags flags)
    {
        _ftlLocked = detail.FtlLock;
        _status.Text = Loc.GetString("wg-ship-admin-move-status",
            ("map", detail.Map),
            ("x", detail.Position.X.ToString("0", CultureInfo.InvariantCulture)),
            ("y", detail.Position.Y.ToString("0", CultureInfo.InvariantCulture)),
            ("speed", detail.Speed.ToString("0.#", CultureInfo.InvariantCulture)),
            ("ftl", Loc.GetString(detail.Ftl.Length > 0 ? $"wg-ship-admin-move-ftl-{detail.Ftl.ToLowerInvariant()}" : "wg-ship-admin-move-ftl-ready")));
        _ftlLock.Text = Loc.GetString(detail.FtlLock ? "wg-ship-admin-move-ftl-lock-off" : "wg-ship-admin-move-ftl-lock-on");

        var hasTarget = _targetGrids.Count > 0;
        Gate(_toMe, WGShipAdminAction.BringToMe, flags);
        Gate(_toPosition, WGShipAdminAction.SendTo, flags, TryPosition(out _));
        Gate(_nextTo, WGShipAdminAction.SendTo, flags, hasTarget);
        Gate(_dockAt, WGShipAdminAction.DockAt, flags, hasTarget);
        Gate(_undock, WGShipAdminAction.Undock, flags, (detail.Flags & WGShipAdminRowFlags.Docked) != 0);
        Gate(_halt, WGShipAdminAction.Halt, flags);
        Gate(_clearPilots, WGShipAdminAction.ClearPilots, flags);
        Gate(_resetFtl, WGShipAdminAction.ResetFtl, flags, detail.Ftl.Length > 0);
        Gate(_ftlLock, WGShipAdminAction.FtlLock, flags);
    }

    private NetEntity? Target()
    {
        var index = _targets.SelectedId;
        return index >= 0 && index < _targetGrids.Count ? _targetGrids[index] : null;
    }

    private bool TryPosition(out Vector2 position)
    {
        position = default;
        if (!float.TryParse(_x.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(_y.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !float.IsFinite(x) || !float.IsFinite(y))
            return false;

        position = new Vector2(x, y);
        return true;
    }

    private void SendMove(WGShipAdminMoveKind kind, NetEntity? target, Vector2 position)
    {
        Send(new WGShipAdminMoveMessage(Grid, kind, target, position));
    }

    private static ConfirmButton Confirm(string key)
    {
        return new ConfirmButton
        {
            Text = Loc.GetString(key),
            ConfirmationText = Loc.GetString("wg-ship-admin-confirm"),
            StyleClasses = { "ButtonSquare" },
        };
    }

    private Button Plain(string key, WGShipAdminAction action)
    {
        var button = new Button { Text = Loc.GetString(key), ToolTip = Loc.GetString($"{key}-tooltip"), StyleClasses = { "ButtonSquare" } };
        button.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, action));
        return button;
    }
}
