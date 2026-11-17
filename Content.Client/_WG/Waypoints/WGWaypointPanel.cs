using System.Linq;
using System.Numerics;
using Content.Client._WG.CombatConsole;
using Content.Shared._WG.Waypoints;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Waypoints;

/// <summary>The radar's waypoint list and its form for typing or picking coordinates.</summary>
public sealed class WGWaypointPanel : PanelContainer
{
    private const float ListHeight = 180;

    private readonly LineEdit _nameEdit;
    private readonly LineEdit _xEdit;
    private readonly LineEdit _yEdit;
    private readonly Button _pick;
    private readonly Button _add;
    private readonly Button _fly;
    private readonly Label _status;
    private readonly Label _empty;
    private readonly BoxContainer _rows;

    private readonly List<WGWaypoint> _shown = new();
    private NetEntity? _shownMap;
    private bool _instrument;
    private bool _canFly;

    public event Action<string, Vector2>? AddRequested;
    public event Action<int>? RemoveRequested;
    public event Action<Vector2>? FlyRequested;
    public event Action<bool>? PickToggled;

    public WGWaypointPanel()
    {
        Name = "WgWaypointPanel";
        SetWidth = 270;
        MouseFilter = MouseFilterMode.Stop;
        PanelOverride = new StyleBoxFlat(Color.FromHex("#181818F0")) { ContentMarginLeftOverride = 6, ContentMarginRightOverride = 6, ContentMarginTopOverride = 6, ContentMarginBottomOverride = 6 };

        _nameEdit = new LineEdit
        {
            Name = "WgWaypointName",
            PlaceHolder = Loc.GetString("wg-waypoint-name"),
            HorizontalExpand = true,
            IsValid = text => text.Length <= WGWaypointRules.MaxNameLength,
        };
        _xEdit = new LineEdit { Name = "WgWaypointX", PlaceHolder = Loc.GetString("wg-waypoint-x"), HorizontalExpand = true };
        _yEdit = new LineEdit { Name = "WgWaypointY", PlaceHolder = Loc.GetString("wg-waypoint-y"), HorizontalExpand = true };
        _pick = new Button
        {
            Name = "WgWaypointPick",
            Text = Loc.GetString("wg-waypoint-pick"),
            ToolTip = Loc.GetString("wg-waypoint-pick-tooltip"),
            ToggleMode = true,
            HorizontalExpand = true,
        };
        _add = new Button
        {
            Name = "WgWaypointAdd",
            Text = Loc.GetString("wg-waypoint-add"),
            ToolTip = Loc.GetString("wg-waypoint-add-tooltip"),
            HorizontalExpand = true,
        };
        _fly = new Button
        {
            Name = "WgWaypointFly",
            Text = Loc.GetString("wg-waypoint-fly"),
            ToolTip = Loc.GetString("wg-waypoint-fly-tooltip"),
            HorizontalExpand = true,
        };
        _status = new Label { Name = "WgWaypointStatus", ClipText = true, Visible = false };
        _empty = new Label { Text = Loc.GetString("wg-waypoint-empty"), FontColorOverride = Color.Gray };
        _rows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 2 };

        _pick.OnToggled += args => PickToggled?.Invoke(args.Pressed);
        _add.OnPressed += _ => SubmitAdd();
        _fly.OnPressed += _ => SubmitFly();

        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Children =
            {
                _nameEdit,
                new BoxContainer { SeparationOverride = 4, Children = { _xEdit, _yEdit } },
                new BoxContainer { SeparationOverride = 4, Children = { _pick, _add, _fly } },
                _status,
                _empty,
                // A scroll container measures as zero height unless told otherwise, which would hide every row.
                new ScrollContainer { HScrollEnabled = false, ReturnMeasure = true, MaxHeight = ListHeight, Children = { _rows } },
            },
        });
    }

    /// <summary>Takes the helm's instrument palette, now and for the rows built later.</summary>
    public void UseInstrumentTheme()
    {
        if (_instrument)
            return;
        _instrument = true;
        PanelOverride = null;
        WGInstrumentTheme.Apply(this);
    }

    /// <summary>Shows the grid's waypoints, rebuilding the rows only when something changed.</summary>
    public void UpdateWaypoints(IReadOnlyList<WGWaypoint> waypoints, NetEntity? map, bool canFly)
    {
        if (canFly == _canFly && map == _shownMap && _shown.SequenceEqual(waypoints))
            return;

        _shown.Clear();
        _shown.AddRange(waypoints);
        _shownMap = map;
        _canFly = canFly;
        _fly.Visible = canFly;
        _empty.Visible = _shown.Count == 0;
        _rows.RemoveAllChildren();
        foreach (var waypoint in _shown)
            _rows.AddChild(BuildRow(waypoint));
        if (_instrument)
            WGInstrumentTheme.Apply(_rows);
    }

    /// <summary>Fills the form from a click on the radar and ends the pick.</summary>
    public void SetPicked(Vector2 position)
    {
        _xEdit.Text = $"{position.X:0.0}";
        _yEdit.Text = $"{position.Y:0.0}";
        CancelPick();
    }

    /// <summary>Ends a pick; setting Pressed from code doesn't raise the button's toggle event.</summary>
    public void CancelPick()
    {
        _pick.Pressed = false;
        PickToggled?.Invoke(false);
    }

    private BoxContainer BuildRow(WGWaypoint waypoint)
    {
        var onMap = waypoint.Map == _shownMap;
        var fly = new Button
        {
            Name = $"WgWaypointFly{waypoint.Id}",
            Text = Loc.GetString("wg-waypoint-go"),
            ToolTip = Loc.GetString(onMap ? "wg-waypoint-fly-tooltip" : "wg-waypoint-other-map"),
            Disabled = !onMap,
            Visible = _canFly,
        };
        fly.OnPressed += _ => Fly(waypoint.Position);
        var remove = new Button
        {
            Name = $"WgWaypointRemove{waypoint.Id}",
            Text = Loc.GetString("wg-waypoint-remove"),
            ToolTip = Loc.GetString("wg-waypoint-remove-tooltip"),
        };
        remove.OnPressed += _ => RemoveRequested?.Invoke(waypoint.Id);

        return new BoxContainer
        {
            SeparationOverride = 4,
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    Modulate = onMap ? Color.White : Color.Gray,
                    Children =
                    {
                        new Label { Text = waypoint.Name, ClipText = true },
                        new Label
                        {
                            Text = onMap
                                ? Loc.GetString("wg-waypoint-position", ("x", $"{waypoint.Position.X:0.0}"), ("y", $"{waypoint.Position.Y:0.0}"))
                                : Loc.GetString("wg-waypoint-other-map"),
                            ClipText = true,
                            FontColorOverride = Color.Gray,
                        },
                    },
                },
                fly,
                remove,
            },
        };
    }

    private bool TryReadPosition(out Vector2 position)
    {
        if (WGWaypointRules.TryParsePosition(_xEdit.Text, _yEdit.Text, out position))
            return true;

        ShowStatus(Loc.GetString("wg-waypoint-status-invalid"));
        return false;
    }

    private void SubmitAdd()
    {
        if (_shown.Count >= WGWaypointRules.MaxWaypoints)
        {
            ShowStatus(Loc.GetString("wg-waypoint-status-full", ("max", WGWaypointRules.MaxWaypoints)));
            return;
        }

        if (!TryReadPosition(out var position))
            return;

        AddRequested?.Invoke(_nameEdit.Text, position);
        _nameEdit.Text = _xEdit.Text = _yEdit.Text = string.Empty;
        _status.Visible = false;
    }

    private void SubmitFly()
    {
        if (TryReadPosition(out var position))
            Fly(position);
    }

    private void Fly(Vector2 position)
    {
        FlyRequested?.Invoke(position);
        ShowStatus(Loc.GetString("wg-waypoint-status-flying", ("x", $"{position.X:0.0}"), ("y", $"{position.Y:0.0}")));
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Visible = true;
    }
}
