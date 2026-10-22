using Content.Client._WF.MappingMode.Modes;
using Content.Client._WF.MappingMode.Palette;
using Content.Shared.Decals;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.MappingMode.QuickSelect;

/// <summary>
/// The quick-select bar over the bottom of the viewport: a set dropdown with new, rename and delete, page tabs, the
/// "add picks" toggle and ten slots of the shown page. Mouse only; the number keys belong to the build modes.
/// </summary>
public sealed partial class WFMappingQuickBar : PanelContainer
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    private readonly WFMappingQuickSelectSystem _system;
    private readonly WFMappingBuildModeSystem _modes;
    private readonly WFMappingPaletteIcons _icons = new();
    private readonly OptionButton _setMenu;
    private readonly BoxContainer _pageTabs;
    private readonly Button _picks;
    private readonly WFMappingQuickSlot[] _slots = new WFMappingQuickSlot[WFMappingQuickSelectStore.SlotsPerPage];

    public WFMappingQuickBar(WFMappingQuickSelectSystem system, WFMappingBuildModeSystem modes)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _modes = modes;

        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#16161A").WithAlpha(0.9f),
            BorderColor = Color.FromHex("#4a4a55"),
            BorderThickness = new Thickness(1),
        };

        var column = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(4),
        };
        AddChild(column);

        var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        column.AddChild(top);

        _setMenu = new OptionButton { MinWidth = 120, ToolTip = Loc.GetString("wf-mapping-quickselect-set-tooltip") };
        _setMenu.OnItemSelected += args =>
        {
            _system.Store.SelectSet(args.Id);
            _system.Modified();
        };
        top.AddChild(_setMenu);
        top.AddChild(SmallButton("wf-mapping-quickselect-set-new", "wf-mapping-quickselect-set-new-tooltip", NewSet));
        top.AddChild(SmallButton("wf-mapping-quickselect-set-rename", "wf-mapping-quickselect-set-rename-tooltip",
            RenameSet));
        top.AddChild(SmallButton("wf-mapping-quickselect-set-delete", "wf-mapping-quickselect-set-delete-tooltip",
            DeleteSet));

        _pageTabs = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 2,
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalExpand = true,
        };
        top.AddChild(_pageTabs);

        _picks = new Button
        {
            Text = Loc.GetString("wf-mapping-quickselect-picks"),
            ToolTip = Loc.GetString("wf-mapping-quickselect-picks-tooltip"),
            ToggleMode = true,
        };
        _picks.OnToggled += args =>
        {
            _system.Store.AddPicks = args.Pressed;
            _system.Modified();
        };
        top.AddChild(_picks);

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        column.AddChild(row);
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = new WFMappingQuickSlot(i);
            slot.Clicked += OnSlotClicked;
            slot.RightClicked += OnSlotRightClicked;
            slot.Dropped += OnSlotDropped;
            _slots[i] = slot;
            row.AddChild(slot);
        }

        _system.Changed += Refresh;
        _modes.EntryChanged += OnEntryChanged;
        Refresh();
    }

    /// <summary>Stops following the store and the selected entry; called when the screen unloads.</summary>
    public void Unbind()
    {
        _system.Changed -= Refresh;
        _modes.EntryChanged -= OnEntryChanged;
    }

    private void OnEntryChanged(WFMappingPaletteEntry? entry)
    {
        RefreshSlots();
    }

    private void Refresh()
    {
        var store = _system.Store;

        _setMenu.Clear();
        for (var i = 0; i < store.Sets.Count; i++)
        {
            _setMenu.AddItem(store.Sets[i].Name, i);
        }

        _setMenu.SelectId(store.ActiveSet);

        _pageTabs.RemoveAllChildren();
        var set = store.Current;
        var group = new ButtonGroup();
        for (var i = 0; i < set.Pages.Count; i++)
        {
            var page = i;
            var tab = new Button
            {
                Text = Loc.GetString("wf-mapping-quickselect-page", ("number", i + 1)),
                ToolTip = Loc.GetString("wf-mapping-quickselect-page-tooltip"),
                ToggleMode = true,
                Group = group,
                Pressed = i == set.ActivePage,
            };
            tab.OnPressed += _ =>
            {
                _system.Store.SelectPage(page);
                _system.Modified();
            };
            tab.OnKeyBindDown += args =>
            {
                if (args.Function != EngineKeyFunctions.UIRightClick)
                    return;

                args.Handle();
                WFMappingEntryMenu.Open((Loc.GetString("wf-mapping-quickselect-page-delete", ("number", page + 1)),
                    () => DeletePage(page)));
            };
            _pageTabs.AddChild(tab);
        }

        if (set.Pages.Count < WFMappingQuickSelectStore.MaxPages)
            _pageTabs.AddChild(SmallButton("wf-mapping-quickselect-page-add", "wf-mapping-quickselect-page-add-tooltip",
                AddPage));

        _picks.Pressed = store.AddPicks;
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        var page = _system.Store.CurrentPage;
        var selected = _modes.Entry;
        for (var i = 0; i < _slots.Length; i++)
        {
            if (page[i] is not { } slot)
            {
                _slots[i].Set(null, Loc.GetString("wf-mapping-quickselect-slot-empty"), false, true, false);
                continue;
            }

            var known = Known(slot.Entry);
            var name = slot.Label ?? _icons.Name(slot.Entry);
            var tooltip = known
                ? Loc.GetString("wf-mapping-quickselect-slot-tooltip", ("name", name), ("id", slot.Entry.Id))
                : Loc.GetString("wf-mapping-quickselect-slot-missing", ("id", slot.Entry.Id));
            _slots[i].Set(known ? _icons.Get(slot.Entry) : null, tooltip, true, known, selected == slot.Entry);
        }
    }

    private bool Known(WFMappingPaletteEntry entry)
    {
        return entry.Kind switch
        {
            WFMappingPaletteKind.Tile => _tileDefs.TryGetDefinition(entry.Id, out _),
            WFMappingPaletteKind.Decal => _prototypes.HasIndex<DecalPrototype>(entry.Id),
            _ => _prototypes.HasIndex<EntityPrototype>(entry.Id),
        };
    }

    private void OnSlotClicked(WFMappingQuickSlot slot)
    {
        if (_system.Store.CurrentPage[slot.Index] is not { } entry)
            return;

        if (!Known(entry.Entry))
        {
            _system.Popup("wf-mapping-quickselect-unknown", ("id", entry.Entry.Id));
            return;
        }

        _system.Place(entry.Entry);
    }

    private void OnSlotRightClicked(WFMappingQuickSlot slot)
    {
        var store = _system.Store;
        if (store.CurrentPage[slot.Index] is not { } entry)
            return;

        var items = new List<(string, Action)>
        {
            (Loc.GetString("wf-mapping-quickselect-slot-remove"), () =>
            {
                _system.Store.Remove(slot.Index);
                _system.Modified();
            }),
            (Loc.GetString("wf-mapping-quickselect-slot-rename"), () =>
                OpenNamePrompt("wf-mapping-quickselect-slot-rename-title", entry.Label ?? _icons.Name(entry.Entry),
                    name =>
                    {
                        _system.Store.SetLabel(slot.Index, name);
                        _system.Modified();
                    })),
        };

        if (entry.Label != null)
        {
            items.Add((Loc.GetString("wf-mapping-quickselect-slot-clear-label"), () =>
            {
                _system.Store.SetLabel(slot.Index, null);
                _system.Modified();
            }));
        }

        var set = store.Current;
        for (var i = 0; i < set.Pages.Count; i++)
        {
            if (i == set.ActivePage)
                continue;

            var page = i;
            items.Add((Loc.GetString("wf-mapping-quickselect-slot-move", ("number", i + 1)), () =>
            {
                if (_system.Store.MoveToPage(slot.Index, page))
                    _system.Modified();
                else
                    _system.Popup("wf-mapping-quickselect-page-full");
            }));
        }

        WFMappingEntryMenu.Open(items.ToArray());
    }

    private void OnSlotDropped(WFMappingQuickSlot from, WFMappingQuickSlot to)
    {
        _system.Store.Swap(from.Index, to.Index);
        _system.Modified();
    }

    private void NewSet()
    {
        if (_system.Store.Sets.Count >= WFMappingQuickSelectStore.MaxSets)
        {
            _system.Popup("wf-mapping-quickselect-sets-full", ("max", WFMappingQuickSelectStore.MaxSets));
            return;
        }

        OpenNamePrompt("wf-mapping-quickselect-set-new-title",
            Loc.GetString("wf-mapping-quickselect-set-new-name", ("number", _system.Store.Sets.Count + 1)),
            name =>
            {
                if (_system.Store.AddSet(name))
                    _system.Modified();
            });
    }

    private void RenameSet()
    {
        var index = _system.Store.ActiveSet;
        OpenNamePrompt("wf-mapping-quickselect-set-rename-title", _system.Store.Current.Name, name =>
        {
            if (_system.Store.RenameSet(index, name))
                _system.Modified();
        });
    }

    private void DeleteSet()
    {
        var store = _system.Store;
        if (store.Sets.Count <= 1)
        {
            _system.Popup("wf-mapping-quickselect-set-last");
            return;
        }

        var index = store.ActiveSet;
        WFMappingEntryMenu.Open((Loc.GetString("wf-mapping-quickselect-set-delete-confirm", ("name", store.Current.Name)),
            () =>
            {
                if (_system.Store.DeleteSet(index))
                    _system.Modified();
            }));
    }

    private void AddPage()
    {
        if (_system.Store.AddPage())
            _system.Modified();
        else
            _system.Popup("wf-mapping-quickselect-pages-full", ("max", WFMappingQuickSelectStore.MaxPages));
    }

    private void DeletePage(int page)
    {
        if (_system.Store.DeletePage(page))
            _system.Modified();
        else
            _system.Popup("wf-mapping-quickselect-page-last");
    }

    private static Button SmallButton(string textKey, string tooltipKey, Action action)
    {
        var button = new Button
        {
            Text = Loc.GetString(textKey),
            ToolTip = Loc.GetString(tooltipKey),
            MinWidth = 24,
        };
        button.OnPressed += _ => action();
        return button;
    }

    /// <summary>A small popup at the mouse with a name field; Enter or OK hands the trimmed name over.</summary>
    private static void OpenNamePrompt(string titleKey, string initial, Action<string> done)
    {
        var popup = new Popup();
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1b1b1e"),
                BorderColor = Color.FromHex("#4a4a55"),
                BorderThickness = new Thickness(1),
            },
        };
        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            MinWidth = 220,
            Margin = new Thickness(6),
        };
        box.AddChild(new Label { Text = Loc.GetString(titleKey) });

        var field = new LineEdit { Text = initial, HorizontalExpand = true, SelectAllOnFocus = true };
        field.IsValid = text => text.Length <= WFMappingQuickSelectStore.MaxNameLength;
        box.AddChild(field);

        var ok = new Button
        {
            Text = Loc.GetString("wf-mapping-quickselect-name-ok"),
            HorizontalAlignment = HAlignment.Right,
        };
        box.AddChild(ok);

        void Submit()
        {
            var name = WFMappingQuickSelectStore.NormalizeName(field.Text);
            popup.Close();
            if (name != null)
                done(name);
        }

        ok.OnPressed += _ => Submit();
        field.OnTextEntered += _ => Submit();

        panel.AddChild(box);
        popup.AddChild(panel);
        popup.OpenAtMouse();
        field.GrabKeyboardFocus();
    }
}
