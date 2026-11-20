using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.ShipAdmin.Tabs;

/// <summary>Rescuing a ship: bring it here, send it somewhere, dock it, stop it, unstick it, and free its helm and jump drive.</summary>
public sealed class WGShipAdminMoveTab : WGShipAdminTab
{
    private readonly WGShipAdminText _status;
    private readonly ConfirmButton _toMe;
    private readonly LineEdit _x;
    private readonly LineEdit _y;
    private readonly ConfirmButton _toPosition;
    private readonly WGShipAdminDropdown _targets;
    private readonly WGShipAdminDropdown _docks;
    private readonly ConfirmButton _nextTo;
    private readonly ConfirmButton _dockAt;
    private readonly ConfirmButton _unstick;
    private readonly ConfirmButton _orbit;
    private readonly Button _undock;
    private readonly Button _halt;
    private readonly Button _clearPilots;
    private readonly Button _ftlLock;
    private readonly Button _resetFtl;

    private readonly List<NetEntity> _targetGrids = new();
    private readonly List<string> _targetNames = new();

    /// <summary>The docks the dock dropdown lists, after its first entry, "any free dock".</summary>
    private readonly List<WGShipAdminDock> _dockList = new();

    /// <summary>The grid the dock list was asked for; the reply for any other is stale.</summary>
    private NetEntity? _docksFor;

    /// <summary>The ship the dock list was last built for.</summary>
    private NetEntity _docksBuiltFor;
    private bool _ftlLocked;
    private AdminFlags _flags;
    private WGShipAdminDetail? _detail;

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
        _x.OnTextChanged += _ => _toPosition.Disabled = !TryPosition(out var typedX);
        _y.OnTextChanged += _ => _toPosition.Disabled = !TryPosition(out var typedY);
        Body.AddChild(Row(_x, _y, _toPosition));

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-move-to-grid-heading")));
        Body.AddChild(Note(Loc.GetString("wg-ship-admin-move-to-grid-note")));
        _targets = Dropdown();
        _targets.OnItemSelected += _ => RequestDocks(true);
        _nextTo = Confirm("wg-ship-admin-move-next-to");
        _nextTo.OnPressed += _ => SendMove(WGShipAdminMoveKind.NextTo, Target(), Vector2.Zero);
        Body.AddChild(Row(_targets, _nextTo));

        _docks = Dropdown();
        _dockAt = Confirm("wg-ship-admin-move-dock-at");
        _dockAt.OnPressed += _ =>
        {
            if (Target() is { } target)
                Send(new WGShipAdminDockMessage(Grid, target, PickedDock()));
        };
        _docks.OnItemSelected += _ =>
        {
            _dockAt.WgCancel();
            GateDock();
        };
        Body.AddChild(Row(_docks, _dockAt));
        _docks.AddItem(Loc.GetString("wg-ship-admin-move-dock-any"), 0);

        Body.AddChild(Heading(Loc.GetString("wg-ship-admin-move-free-heading")));
        _unstick = Confirm("wg-ship-admin-move-unstick");
        _unstick.ToolTip = Loc.GetString("wg-ship-admin-move-unstick-tooltip");
        _unstick.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.Unstick));
        _orbit = Confirm("wg-ship-admin-move-orbit");
        _orbit.ToolTip = Loc.GetString("wg-ship-admin-move-orbit-tooltip");
        _orbit.OnPressed += _ => Send(new WGShipAdminActionMessage(Grid, WGShipAdminAction.ReturnToOrbit));
        Body.AddChild(Row(_unstick, _orbit));

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
        if (!SameTargets(ships))
        {
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

        // Each list, sent on Refresh and every few seconds, also asks again which docks are in use.
        RequestDocks(Target() != _docksFor);
    }

    /// <summary>Fills the dock dropdown with a grid's docks, marking those in use. Unchanged docks leave it alone.</summary>
    public void SetDocks(NetEntity? target, List<WGShipAdminDock> docks)
    {
        // Which docks are pickable depends on the ship being moved, so a new ship rebuilds the list too.
        if (target != _docksFor || SameDocks(docks) && _docksBuiltFor == Grid)
            return;

        _docksBuiltFor = Grid;

        var picked = PickedDock();
        var stillPickable = picked == null;
        _docks.Clear();
        _dockList.Clear();
        _docks.AddItem(Loc.GetString("wg-ship-admin-move-dock-any"), 0);
        foreach (var dock in docks)
        {
            var id = _dockList.Count + 1;
            var free = dock.DockedWithGrid == null;
            _docks.AddItem(free
                ? Loc.GetString("wg-ship-admin-move-dock-free", ("dock", dock.Name))
                : Loc.GetString("wg-ship-admin-move-dock-used", ("dock", dock.Name), ("with", dock.DockedWith)), id);

            // A dock this ship holds can be picked again; one another grid holds can't.
            var pickable = free || dock.DockedWithGrid == Grid;
            _docks.SetItemDisabled(_docks.GetIdx(id), !pickable);
            if (dock.Dock == picked && pickable)
            {
                _docks.SelectId(id);
                stillPickable = true;
            }

            _dockList.Add(dock);
        }

        // The dock picked at the first press is gone or taken: the second press must not dock anywhere else.
        if (!stillPickable)
            _dockAt.WgCancel();

        GateDock();
    }

    /// <summary>The docks in the dropdown, for tests.</summary>
    public IReadOnlyList<WGShipAdminDock> Docks => _dockList;

    /// <summary>
    /// Asks for the target's docks. A new target empties the dropdown and drops a pending Dock at; the same target
    /// keeps the dropdown, and its pick, until the reply.
    /// </summary>
    private void RequestDocks(bool newTarget)
    {
        if (newTarget)
        {
            _docksFor = Target();
            _dockList.Clear();
            _docks.Clear();
            _docks.AddItem(Loc.GetString("wg-ship-admin-move-dock-any"), 0);
            _dockAt.WgCancel();
            _nextTo.WgCancel();
            GateDock();
        }

        if (_docksFor is { } target)
            Send(new WGShipAdminDocksRequestMessage(target));
    }

    private bool SameDocks(List<WGShipAdminDock> docks)
    {
        if (docks.Count != _dockList.Count)
            return false;

        for (var i = 0; i < docks.Count; i++)
        {
            if (docks[i].Dock != _dockList[i].Dock || docks[i].Name != _dockList[i].Name || docks[i].DockedWithGrid != _dockList[i].DockedWithGrid)
                return false;
        }

        return true;
    }

    private NetEntity? PickedDock()
    {
        var index = _docks.SelectedId - 1;
        return index >= 0 && index < _dockList.Count ? _dockList[index].Dock : null;
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
        // Docking or undocking this ship changes which docks are in use.
        var redock = _detail == null || _detail.Grid != detail.Grid || !_detail.DockedTo.SequenceEqual(detail.DockedTo);
        _detail = detail;
        if (redock && _docksFor != null)
            RequestDocks(false);

        _flags = flags;
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
        GateDock();
        Gate(_unstick, WGShipAdminAction.Unstick, flags);
        Gate(_orbit, WGShipAdminAction.ReturnToOrbit, flags, detail.OnPlanet);
        Gate(_undock, WGShipAdminAction.Undock, flags, (detail.Flags & WGShipAdminRowFlags.Docked) != 0);
        Gate(_halt, WGShipAdminAction.Halt, flags);
        Gate(_clearPilots, WGShipAdminAction.ClearPilots, flags);
        Gate(_resetFtl, WGShipAdminAction.ResetFtl, flags, detail.Ftl.Length > 0);
        Gate(_ftlLock, WGShipAdminAction.FtlLock, flags);
    }

    private void GateDock()
    {
        Gate(_dockAt, WGShipAdminAction.DockAt, _flags, Target() != null && Target() != Grid);
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
