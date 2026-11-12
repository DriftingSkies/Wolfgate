using System.Linq;
using System.Numerics;
using Content.Shared._WF.Hideouts.Blueprint;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Hideouts.Blueprint;

/// <summary>
/// The blueprint: a searchable palette of brushes with their pictures down the left, the plan to paint on, and the
/// plan's price with the button that has it built. The outpost console's Blueprint tab, and the shipwright's Interior
/// window over the hull in the bay.
/// </summary>
public sealed class WFBlueprintPanel : BoxContainer
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;

    private static readonly Color GoodColor = Color.FromHex("#6FCF6F");
    private static readonly Color BadColor = Color.FromHex("#E05A4F");

    private static readonly (WFBlueprintTool Tool, string Name)[] Tools =
    {
        (WFBlueprintTool.Brush, "wf-blueprint-tool-brush"),
        (WFBlueprintTool.Box, "wf-blueprint-tool-box"),
        (WFBlueprintTool.Outline, "wf-blueprint-tool-outline"),
        (WFBlueprintTool.Unplan, "wf-blueprint-tool-unplan"),
    };

    private static readonly Direction[] Facings = { Direction.South, Direction.West, Direction.North, Direction.East };

    /// <summary>The most brushes listed at once; a search narrows a longer list.</summary>
    private const int Listed = 160;

    /// <summary>The tab wants what stands on the grid sent afresh.</summary>
    public event Action? OnRequestCanvas;

    /// <summary>The builder has the plan built.</summary>
    public event Action<List<WFBlueprintStroke>>? OnApply;

    private readonly WFBlueprintCanvas _canvas;
    private readonly BoxContainer _palette;
    private readonly LineEdit _search;
    private readonly OptionButton _groups;
    private readonly Label _cost;
    private readonly Label _balance;
    private readonly Label _hover;
    private readonly Label _selected;
    private readonly Label _hint;
    private readonly Button _apply;
    private readonly Button _undo;
    private readonly Button _clear;
    private readonly Button _facing;
    private readonly Dictionary<string, Button> _brushButtons = new();
    private readonly Dictionary<WFBlueprintTool, Button> _toolButtons = new();

    private List<WFBlueprintEntry> _entries = new();
    private readonly List<LocId?> _groupIds = new();
    private Dictionary<string, int> _prices = new();
    private int _balanceValue;
    private bool _canBuild;
    private bool _requested;
    private bool _ship;
    private EntityUid? _grid;

    public WFBlueprintPanel()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 8;
        Margin = new Thickness(8);

        // Palette.
        var left = new BoxContainer { Orientation = LayoutOrientation.Vertical, MinWidth = 290, MaxWidth = 290, SeparationOverride = 4 };
        left.AddChild(new Label { Text = Loc.GetString("wf-blueprint-palette"), StyleClasses = { "LabelHeading" } });
        _search = new LineEdit { PlaceHolder = Loc.GetString("wf-blueprint-search") };
        _search.OnTextChanged += _ => BuildPalette();
        left.AddChild(_search);
        _groups = new OptionButton();
        _groups.OnItemSelected += args =>
        {
            _groups.SelectId(args.Id);
            BuildPalette();
        };
        left.AddChild(_groups);
        _palette = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        left.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _palette } });
        AddChild(left);

        // Plan.
        var middle = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
        var bar = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
        foreach (var (tool, name) in Tools)
        {
            var button = new Button { Text = Loc.GetString(name), ToggleMode = true, ToolTip = Loc.GetString(name + "-tip") };
            var picked = tool;
            button.OnPressed += _ => SetTool(picked);
            _toolButtons[tool] = button;
            bar.AddChild(button);
        }

        _facing = new Button { ToolTip = Loc.GetString("wf-blueprint-facing-tip") };
        _facing.OnPressed += _ => Turn();
        bar.AddChild(_facing);
        bar.AddChild(new Control { HorizontalExpand = true });
        var roofs = new CheckBox { Text = Loc.GetString("wf-blueprint-show-roofs") };
        bar.AddChild(roofs);
        var centre = new Button { Text = Loc.GetString("wf-blueprint-centre") };
        bar.AddChild(centre);
        var refresh = new Button { Text = Loc.GetString("wf-blueprint-refresh") };
        refresh.OnPressed += _ => OnRequestCanvas?.Invoke();
        bar.AddChild(refresh);
        middle.AddChild(bar);

        _canvas = new WFBlueprintCanvas { HorizontalExpand = true, VerticalExpand = true, MinSize = new Vector2(320, 320) };
        _canvas.OnPlanChanged += UpdatePlan;
        _canvas.OnHover += index => _hover!.Text = index is { } at ? Loc.GetString("wf-blueprint-hover", ("x", at.X), ("y", at.Y)) : string.Empty;
        roofs.OnToggled += args => _canvas.ShowRoofs = args.Pressed;
        centre.OnPressed += _ => _canvas.Recentre();
        middle.AddChild(new PanelContainer { StyleClasses = { "AngleRect" }, VerticalExpand = true, Children = { _canvas } });

        var foot = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        _selected = new Label { ClipText = true, HorizontalExpand = true };
        _hover = new Label { StyleClasses = { "LabelSubText" }, MinWidth = 90 };
        _cost = new Label();
        _balance = new Label { StyleClasses = { "LabelSubText" } };
        _undo = new Button { Text = Loc.GetString("wf-blueprint-undo") };
        _undo.OnPressed += _ => _canvas.Undo();
        _clear = new Button { Text = Loc.GetString("wf-blueprint-clear") };
        _clear.OnPressed += _ => _canvas.ClearPlan();
        _apply = new Button { Text = Loc.GetString("wf-blueprint-apply"), StyleClasses = { "OpenLeft" } };
        _apply.OnPressed += _ =>
        {
            OnApply?.Invoke(_canvas.Plan.ToList());
            _canvas.ClearPlan();
        };
        foot.AddChild(_selected);
        foot.AddChild(_hover);
        foot.AddChild(_balance);
        foot.AddChild(_cost);
        foot.AddChild(_undo);
        foot.AddChild(_clear);
        foot.AddChild(_apply);
        middle.AddChild(foot);
        _hint = new Label { Text = Loc.GetString("wf-blueprint-hint"), StyleClasses = { "LabelSubText" } };
        middle.AddChild(_hint);
        AddChild(middle);

        LoadEntries();
        SetTool(WFBlueprintTool.Brush);
        UpdateFacing();
        UpdatePlan();
    }

    /// <summary>Turns the panel into a ship's interior plan: no roof brushes, no claim, and floor only over the hull.</summary>
    public void SetShipMode()
    {
        _ship = true;
        _canvas.ShowClaim = false;
        _hint.Text = Loc.GetString("wf-blueprint-hint-ship");
        LoadEntries();
    }

    /// <summary>Points the plan at a grid; the first time, and whenever the grid changes, asks the server what stands on it.</summary>
    public void SetOutpost(EntityUid? grid, int claimMargin, bool canBuild)
    {
        _canvas.SetGrid(grid, claimMargin);
        _canBuild = canBuild && grid != null;
        if (grid != _grid)
        {
            _grid = grid;
            _requested = false;
            _canvas.ClearPlan();
        }

        if (!_requested && grid != null)
        {
            _requested = true;
            OnRequestCanvas?.Invoke();
        }

        UpdatePlan();
    }

    public void SetCanvas(WFBlueprintCanvasMessage message)
    {
        _canvas.SetCells(message.Cells);
        _prices = message.Prices;
        _balanceValue = message.Balance;
        foreach (var (id, button) in _brushButtons)
        {
            if (_canvas.Entries.TryGetValue(id, out var brush))
                button.Text = BrushText(brush);
        }

        UpdatePlan();
    }

    private string BrushText(WFBlueprintEntry brush)
    {
        var price = _prices.GetValueOrDefault(brush.Id, brush.Price);
        return price > 0 ? Loc.GetString("wf-blueprint-brush-priced", ("name", brush.Name), ("price", price)) : brush.Name;
    }

    /// <summary>Reads the palette from the prototypes and lists its sections.</summary>
    private void LoadEntries()
    {
        _entries = WFBlueprintCatalog.All(_proto, _tiles)
            .Where(brush => !_ship || brush.Kind is not (WFBlueprintKind.Roof or WFBlueprintKind.Sky)).ToList();
        _canvas.Entries = _entries.ToDictionary(brush => brush.Id);

        _groups.Clear();
        _groupIds.Clear();
        _groups.AddItem(Loc.GetString("wf-blueprint-group-all"), 0);
        _groupIds.Add(null);
        foreach (var group in _entries.Select(brush => brush.Group).Distinct())
        {
            _groups.AddItem(Loc.GetString(group), _groupIds.Count);
            _groupIds.Add(group);
        }

        BuildPalette();
        if (_canvas.Brush == null || !_canvas.Entries.ContainsKey(_canvas.Brush.Id))
        {
            if (_entries.Count > 0)
                SetBrush(_entries[0]);
        }
    }

    /// <summary>Lists the brushes of the chosen section whose names hold the search text.</summary>
    private void BuildPalette()
    {
        _palette.RemoveAllChildren();
        _brushButtons.Clear();
        var search = _search.Text.Trim();
        var group = _groups.SelectedId >= 0 && _groups.SelectedId < _groupIds.Count ? _groupIds[_groups.SelectedId] : null;
        var shown = _entries.Where(brush => (group == null || brush.Group == group)
            && (search.Length == 0 || brush.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))).ToList();

        LocId? heading = null;
        foreach (var brush in shown.Take(Listed))
        {
            if (heading != brush.Group)
            {
                heading = brush.Group;
                _palette.AddChild(new Label { Text = Loc.GetString(brush.Group), StyleClasses = { "LabelSubText" }, Margin = new Thickness(0, 6, 0, 0) });
            }

            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
            if (WFBlueprintIcons.Get(brush) is { } picture)
            {
                row.AddChild(new TextureRect
                {
                    Texture = picture,
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    SetSize = new Vector2(28, 28),
                    VerticalAlignment = VAlignment.Center,
                });
            }
            else
            {
                row.AddChild(new PanelContainer
                {
                    SetSize = new Vector2(28, 28),
                    VerticalAlignment = VAlignment.Center,
                    PanelOverride = new StyleBoxFlat { BackgroundColor = brush.Color },
                });
            }

            var button = new Button
            {
                Text = BrushText(brush),
                ToggleMode = true,
                HorizontalExpand = true,
                ClipText = true,
                TextAlign = Label.AlignMode.Left,
                Pressed = _canvas.Brush?.Id == brush.Id,
            };
            var picked = brush;
            button.OnPressed += _ => SetBrush(picked);
            _brushButtons[brush.Id] = button;
            row.AddChild(button);
            _palette.AddChild(row);
        }

        if (shown.Count > Listed)
            _palette.AddChild(new Label { Text = Loc.GetString("wf-blueprint-more", ("count", shown.Count - Listed)), StyleClasses = { "LabelSubText" } });
    }

    private void SetBrush(WFBlueprintEntry brush)
    {
        _canvas.Brush = brush;
        foreach (var (id, button) in _brushButtons)
        {
            button.Pressed = id == brush.Id;
        }

        if (_canvas.Tool == WFBlueprintTool.Unplan)
            SetTool(WFBlueprintTool.Brush);
        _selected.Text = Loc.GetString("wf-blueprint-selected", ("name", brush.Name));
    }

    private void SetTool(WFBlueprintTool tool)
    {
        _canvas.Tool = tool;
        foreach (var (id, button) in _toolButtons)
        {
            button.Pressed = id == tool;
        }
    }

    private void Turn()
    {
        _canvas.Facing = Facings[(Array.IndexOf(Facings, _canvas.Facing) + 1) % Facings.Length];
        UpdateFacing();
    }

    private void UpdateFacing()
    {
        _facing.Text = Loc.GetString("wf-blueprint-facing", ("direction", Loc.GetString("wf-blueprint-facing-" + _canvas.Facing.ToString().ToLowerInvariant())));
    }

    private void UpdatePlan()
    {
        var total = 0;
        foreach (var stroke in _canvas.Plan)
        {
            total += _prices.GetValueOrDefault(stroke.Brush);
        }

        var affordable = total <= _balanceValue;
        _cost.Text = Loc.GetString("wf-blueprint-cost", ("count", _canvas.PlanCount), ("max", WFBlueprintLimits.Strokes), ("price", total));
        _cost.FontColorOverride = affordable ? GoodColor : BadColor;
        _balance.Text = Loc.GetString("wf-blueprint-balance", ("balance", _balanceValue));
        _apply.Disabled = !_canBuild || _canvas.PlanCount == 0 || !affordable;
        _undo.Disabled = !_canvas.CanUndo;
        _clear.Disabled = _canvas.PlanCount == 0;
    }
}
