using System.Linq;
using System.Numerics;
using Content.Client._WG.Humanoid;
using Content.Client._WG.Stylesheets;
using Content.Shared._WG.CustomMarkings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client._WG.CustomMarkings.UI;

/// <summary>
/// The player's custom markings as tiles on the creator's Markings tab, the same tiles the marking picker under it
/// uses: pressing one puts it on the character being edited or takes it off, right clicking one edits it, and the
/// first tile draws a new one.
/// </summary>
public sealed partial class CustomMarkingQuickList : BoxContainer
{
    /// <summary>Two rows of tiles; a longer library scrolls.</summary>
    private const float MaxListHeight = 204;

    private const int NewIconScale = 4;

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;

    private readonly GridContainer _grid;

    private readonly CustomMarkingSystem _system;
    private List<CustomMarking> _worn = new();
    private Direction _direction = Direction.South;
    private bool _listening;

    /// <summary>Raised with the new list whenever a tile changes what the character wears.</summary>
    public Action<List<CustomMarking>>? OnWornChanged;

    /// <summary>The character being edited, which the editor draws under the art.</summary>
    public Func<HumanoidCharacterProfile?>? Profile;

    /// <summary>The slot of the character being edited: it is offered its own markings and the shared ones.</summary>
    public Func<int?>? Slot;

    public CustomMarkingQuickList()
    {
        IoCManager.InjectDependencies(this);
        _system = _entMan.System<CustomMarkingSystem>();

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;

        _grid = new GridContainer { Columns = 6, HSeparationOverride = 4, VSeparationOverride = 4 };
        AddChild(new ScrollContainer
        {
            HScrollEnabled = false,
            ReturnMeasure = true,
            MaxHeight = MaxListHeight,
            HorizontalExpand = true,
            Children = { _grid },
        });
    }

    private int MaxWorn => Math.Min(_cfg.GetCVar(CustomMarkingCVars.MaxWorn), CustomMarkingRules.MaxWornCap);

    /// <summary>Shows which markings the character wears. Call when the profile is loaded or changed elsewhere.</summary>
    public void SetWorn(IEnumerable<CustomMarking> worn)
    {
        _worn = worn.ToList();
        Rebuild();
    }

    /// <summary>Turns the tiles to face the way the preview does.</summary>
    public void SetDirection(Direction direction)
    {
        _direction = direction;
        foreach (var child in _grid.Children)
        {
            if (child is WolfgateMarkingTile tile)
                tile.SetDirection(direction);
        }
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        _system.LibraryUpdated += Rebuild;
        _system.ArtLoaded += OnArtLoaded;
        _listening = true;

        _system.RequestLibrary();
        Rebuild();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        if (!_listening)
            return;

        _system.LibraryUpdated -= Rebuild;
        _system.ArtLoaded -= OnArtLoaded;
        _listening = false;
    }

    protected override void Resized()
    {
        base.Resized();

        var columns = Math.Max(1, (int) (Width / WolfgateMarkingTile.TileWidth));
        if (_grid.Columns != columns)
            _grid.Columns = columns;
    }

    private void OnArtLoaded(string hash)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        _grid.RemoveAllChildren();

        var library = _system.Library;
        _grid.AddChild(NewTile(library));
        if (library != null)
        {
            var slot = Slot?.Invoke();
            foreach (var entry in library)
            {
                if (entry.OfferedTo(slot))
                    AddTile(new CustomMarking(entry.Hash, entry.Placement), entry.Name, entry);
            }
        }

        // Worn markings with no library entry offered here still get a tile, so they can come off.
        foreach (var marking in _worn)
        {
            if (library == null || !library.Any(entry => entry.OfferedTo(Slot?.Invoke()) && entry.Hash == marking.Hash && entry.Placement == marking.Placement))
                AddTile(marking, Loc.GetString("wg-custom-marking-default-name"), null);
        }
    }

    /// <summary>The tile that opens the editor on a blank marking.</summary>
    private Control NewTile(IReadOnlyCollection<CustomMarkingEntry>? library)
    {
        var limit = _cfg.GetCVar(CustomMarkingCVars.LibraryLimit);
        var reason = library == null
            ? Loc.GetString("wg-custom-marking-reason-loading")
            : library.Count >= limit
                ? Loc.GetString("wg-custom-marking-reason-library-full", ("limit", limit))
                : null;

        var tooltip = Loc.GetString("wg-custom-marking-tile-new-tooltip");
        var tile = new ContainerButton
        {
            MinSize = new Vector2(WolfgateMarkingTile.TileWidth - 4, 96),
            Disabled = reason != null,
            ToolTip = reason == null ? tooltip : $"{tooltip}\n{reason}",
            Children =
            {
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    SeparationOverride = 2,
                    Margin = new Thickness(4, 4, 4, 2),
                    Children =
                    {
                        new TextureRect
                        {
                            Texture = _entMan.System<SpriteSystem>().Frame0(new SpriteSpecifier.Rsi(CustomMarkingIconButton.Icons, "add")),
                            TextureScale = new Vector2(NewIconScale, NewIconScale),
                            Stretch = TextureRect.StretchMode.KeepCentered,
                            VerticalExpand = true,
                            StyleClasses = { reason == null ? StyleWolfgate.StyleClassCreatorIcon : StyleWolfgate.StyleClassCreatorIconDisabled },
                        },
                        new Label
                        {
                            Text = Loc.GetString("wg-custom-marking-library-new"),
                            ClipText = true,
                            Align = Label.AlignMode.Center,
                            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
                        },
                    },
                },
            },
        };
        tile.AddStyleClass(WolfgateMarkingTile.StyleClassTile);
        tile.OnPressed += _ => Edit(null, new CustomMarkingArt(), string.Empty, null);
        return tile;
    }

    private void AddTile(CustomMarking marking, string name, CustomMarkingEntry? entry)
    {
        // The art's RSI can only be named once it is here; until then the tile is blank.
        SpriteSpecifier[]? sprites = null;
        var loaded = _system.TryGetArt(marking.Hash, out _);
        if (loaded)
            sprites = new SpriteSpecifier[] { new SpriteSpecifier.Rsi(CustomMarkingResources.PathFor(marking.Hash), CustomMarkingResources.State) };

        var placement = Loc.GetString(CustomMarkingEditorWindow.PlacementName(marking.Placement));
        var worn = _worn.Contains(marking);
        var full = !worn && _worn.Count >= MaxWorn;
        var lines = new List<string>
        {
            Loc.GetString("wg-custom-marking-tile-tooltip", ("name", name), ("placement", placement)),
        };
        if (entry == null)
            lines.Add(Loc.GetString("wg-custom-marking-library-stray"));
        else
            lines.Add(Loc.GetString(entry.Value.Slot == null ? "wg-custom-marking-scope-all" : "wg-custom-marking-scope-character"));
        if (full)
            lines.Add(Loc.GetString("wg-custom-marking-reason-worn-full", ("max", MaxWorn)));
        lines.Add(Loc.GetString(loaded ? "wg-custom-marking-tile-edit" : "wg-custom-marking-reason-loading"));

        var tile = new WolfgateMarkingTile(null, name, sprites, _direction)
        {
            Pressed = worn,
            Disabled = full,
            ToolTip = string.Join("\n", lines),
        };
        tile.OnPressed += _ => Toggle(marking);
        // A full character can't put the marking on, but it can still be edited.
        tile.OnKeyBindDown += args =>
        {
            if (args.Function != EngineKeyFunctions.UIRightClick || !_system.TryReadArt(marking.Hash, out var art))
                return;

            args.Handle();
            if (entry is { } known)
                Edit(known, art, known.Name, null);
            else
                Edit(null, art, string.Empty, marking);
        };
        _grid.AddChild(tile);
    }

    private void Edit(CustomMarkingEntry? entry, CustomMarkingArt art, string name, CustomMarking? replacing)
    {
        if (Profile == null)
            return;

        UserInterfaceManager.GetUIController<CustomMarkingUIController>()
            .OpenEditor(entry, art, name, Profile, worn => OnWornChanged?.Invoke(worn), replacing, Slot?.Invoke());
    }

    private void Toggle(CustomMarking marking)
    {
        if (CustomMarkingRules.Toggle(_worn, marking, MaxWorn))
            OnWornChanged?.Invoke(_worn.ToList());

        Rebuild();
    }
}
