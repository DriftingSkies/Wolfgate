using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;

namespace Content.Client._WF.MappingMode.Palette;

/// <summary>The palette's Tiles tab: search, All, Favourites and Recent, one row per tile.</summary>
public sealed partial class WFMappingTilePalette : BoxContainer
{
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    private enum Filter : byte
    {
        All,
        Favourites,
        Recent,
    }

    private readonly WFMappingPaletteStore _store;
    private readonly WFMappingPaletteIcons _icons;
    private readonly LineEdit _search;
    private readonly BoxContainer _list;
    private readonly Label _empty;
    private readonly Dictionary<string, Button> _rows = new();

    private Filter _filter = Filter.All;
    private string? _selected;

    /// <summary>Raised when a tile is clicked.</summary>
    public event Action<WFMappingPaletteEntry>? EntrySelected;

    /// <summary>Raised from a tile's right-click menu.</summary>
    public event Action<WFMappingPaletteEntry>? QuickSelectRequested;

    /// <summary>Raised after a favourite is toggled, so the store can be saved.</summary>
    public event Action? StoreChanged;

    public WFMappingTilePalette(WFMappingPaletteStore store, WFMappingPaletteIcons icons)
    {
        IoCManager.InjectDependencies(this);
        _store = store;
        _icons = icons;

        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 4;
        Margin = new Thickness(4);
        VerticalExpand = true;

        _search = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("wf-mapping-screen-search"),
        };
        _search.OnTextChanged += _ => Refresh();
        AddChild(_search);

        var filters = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 2 };
        var group = new ButtonGroup();
        foreach (var (filter, key) in new[]
                 {
                     (Filter.All, "wf-mapping-screen-filter-all"),
                     (Filter.Favourites, "wf-mapping-screen-filter-favourites"),
                     (Filter.Recent, "wf-mapping-screen-filter-recent"),
                 })
        {
            var button = new Button
            {
                Text = Loc.GetString(key),
                ToggleMode = true,
                Group = group,
                Pressed = filter == _filter,
                HorizontalExpand = true,
            };
            button.OnPressed += _ =>
            {
                _filter = filter;
                Refresh();
            };
            filters.AddChild(button);
        }

        AddChild(filters);

        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        _list = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 1 };
        scroll.AddChild(_list);
        AddChild(scroll);

        _empty = new Label
        {
            Text = Loc.GetString("wf-mapping-screen-empty"),
            StyleClasses = { "LabelSubText" },
            Visible = false,
        };
        AddChild(_empty);

        Refresh();
    }

    /// <summary>Highlights a tile without raising <see cref="EntrySelected"/>.</summary>
    public void SetSelected(string? id)
    {
        _selected = id;
        foreach (var (rowId, row) in _rows)
        {
            row.Pressed = rowId == id;
        }
    }

    /// <summary>Rebuilds the list for the search and filter.</summary>
    public void Refresh()
    {
        _list.RemoveAllChildren();
        _rows.Clear();

        var search = _search.Text.Trim();
        IEnumerable<ITileDefinition> tiles = _filter switch
        {
            Filter.Favourites => _tileDefs.Where(t => _store.IsFavourite(WFMappingPaletteKind.Tile, t.ID)),
            Filter.Recent => _store.Recent(WFMappingPaletteKind.Tile)
                .Select(id => _tileDefs.TryGetDefinition(id, out var def) ? def : null)
                .Where(def => def != null)
                .Select(def => def!),
            _ => _tileDefs,
        };

        tiles = tiles.Where(t => !t.EditorHidden || _filter != Filter.All);
        if (search.Length > 0)
        {
            tiles = tiles.Where(t => Loc.GetString(t.Name).Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                                     t.ID.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (_filter != Filter.Recent)
            tiles = tiles.OrderBy(t => Loc.GetString(t.Name));

        foreach (var tile in tiles)
        {
            AddRow(tile);
        }

        _empty.Visible = _rows.Count == 0;
    }

    private void AddRow(ITileDefinition tile)
    {
        if (_rows.ContainsKey(tile.ID))
            return;

        var entry = new WFMappingPaletteEntry(WFMappingPaletteKind.Tile, tile.ID);
        var row = new Button
        {
            ToggleMode = true,
            Pressed = tile.ID == _selected,
            HorizontalExpand = true,
            ToolTip = tile.ID,
        };

        var content = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 6 };
        content.AddChild(new TextureRect
        {
            Texture = _icons.Tile(tile.ID),
            SetSize = new Vector2(32, 32),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
        });

        var favourite = _store.IsFavourite(WFMappingPaletteKind.Tile, tile.ID);
        content.AddChild(new Label
        {
            Text = favourite
                ? Loc.GetString("wf-mapping-screen-favourite-name", ("name", Loc.GetString(tile.Name)))
                : Loc.GetString(tile.Name),
            ClipText = true,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        });
        row.AddChild(content);

        row.OnPressed += _ =>
        {
            SetSelected(tile.ID);
            EntrySelected?.Invoke(entry);
        };
        row.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIRightClick)
                return;

            args.Handle();
            OpenMenu(entry);
        };

        _rows[tile.ID] = row;
        _list.AddChild(row);
    }

    private void OpenMenu(WFMappingPaletteEntry entry)
    {
        var favourite = _store.IsFavourite(entry.Kind, entry.Id);
        WFMappingEntryMenu.Open(
            (Loc.GetString("wf-mapping-screen-add-quick-select"), () => QuickSelectRequested?.Invoke(entry)),
            (Loc.GetString(favourite ? "wf-mapping-screen-unfavourite" : "wf-mapping-screen-favourite"), () =>
            {
                _store.ToggleFavourite(entry.Kind, entry.Id);
                StoreChanged?.Invoke();
                Refresh();
            }));
    }
}
