using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client.Resources;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.ShipModules;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using static Content.Client._WG.CombatConsole.WGInstrumentTheme;
using static Robust.Client.UserInterface.StylesheetHelpers;

namespace Content.Client._WG.Drydock;

/// <summary>
/// The ship modules refit: a key per module slot under the refits, and a drawer beside the column listing every module
/// the drydock fits into the chosen slot.
/// </summary>
public sealed partial class WGDrydockConsoleWindow
{
    /// <summary>Width of the module drawer.</summary>
    private const float DrawerWidth = 340;

    /// <summary>Side of a module's icon in the drawer.</summary>
    private const float ModuleIconSize = 48;

    /// <summary>Raised with the slot when a module slot's remove key is pressed.</summary>
    public event Action<int>? OnModuleRemovePressed;

    [Dependency] private IResourceCache _resCache = default!;

    private readonly List<Button> _slotKeys = new();
    private readonly BoxContainer _moduleList = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
    private readonly Label _drawerSlot = new() { HorizontalExpand = true, ClipText = true };
    private readonly Button _drawerRemove = new();
    private BoxContainer _moduleSection = default!;
    private Control _moduleDrawer = default!;

    /// <summary>Slot whose drawer is open, or null.</summary>
    private int? _openSlot;

    /// <summary>What the drawer's list was last built from, so an unchanged state keeps its rows.</summary>
    private string? _drawerKey;

    /// <summary>Builds the slot keys and the drawer; the section goes in the refits panel, the drawer beside the column.</summary>
    private void BuildModules()
    {
        var keys = new GridContainer { Columns = WGShipModulesComponent.SlotCount, HSeparationOverride = 4, HorizontalExpand = true };
        for (var i = 0; i < WGShipModulesComponent.SlotCount; i++)
        {
            var slot = i;
            var key = new Button { ToggleMode = true, HorizontalExpand = true, MinHeight = 56 };
            key.AddStyleClass("WgCompact");
            key.OnPressed += _ => ToggleDrawer(slot);
            _slotKeys.Add(key);
            keys.AddChild(key);
        }

        var title = new Label { Text = Loc.GetString("drydock-ui-modules").ToUpperInvariant(), FontColorOverride = Accent };
        _moduleSection = Column(title, keys);
        _moduleSection.SeparationOverride = 3;

        // The theme clips button text, which leaves a key no wider than its padding without a floor.
        var close = new Button { Text = Loc.GetString("drydock-ui-modules-close"), MinWidth = 72 };
        close.AddStyleClass("WgCompact");
        close.OnPressed += _ => CloseDrawer();
        _drawerRemove.AddStyleClass("WgCompact");
        _drawerRemove.Text = Loc.GetString("drydock-ui-modules-remove");
        _drawerRemove.OnPressed += _ =>
        {
            if (_openSlot is { } slot)
                OnModuleRemovePressed?.Invoke(slot);
        };

        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(_moduleList);
        // The column must expand too, or the list's scroll gets no height.
        var content = Column(Row(_drawerSlot, close), _drawerRemove, scroll);
        content.VerticalExpand = true;
        var drawer = Panel("drydock-ui-modules-drawer", content, expand: true, padding: 10, gap: 4);
        drawer.SetWidth = DrawerWidth;
        drawer.HorizontalExpand = false;
        drawer.Visible = false;
        _moduleDrawer = drawer;
    }

    private void ToggleDrawer(int slot)
    {
        if (_openSlot == slot)
        {
            CloseDrawer();
            return;
        }

        if (_openSlot == null)
            FitWindow(this, Size + new Vector2(DrawerWidth + 6, 0));

        _openSlot = slot;
        _drawerKey = null;
        _moduleDrawer.Visible = true;
        UpdateModules(_state, _blocked);
    }

    private void CloseDrawer()
    {
        if (_openSlot == null)
            return;

        _openSlot = null;
        _moduleDrawer.Visible = false;
        SetSize = Vector2.Max(MinSize, Size - new Vector2(DrawerWidth + 6, 0));
        UpdateModules(_state, _blocked);
    }

    /// <summary>Updates the slot keys and, while it is open, the drawer.</summary>
    private void UpdateModules(WGDrydockConsoleState state, bool blocked)
    {
        _moduleSection.Visible = state.ShipModules && !state.BreakOnly;
        if (!_moduleSection.Visible || state.ShipName == null)
            CloseDrawer();

        for (var i = 0; i < _slotKeys.Count; i++)
        {
            var key = _slotKeys[i];
            var fitted = i < state.FittedModules.Count ? state.FittedModules[i] : null;
            var label = fitted != null && _prototypeManager.TryIndex<WGShipModulePrototype>(fitted, out var module)
                ? Loc.GetString(module.Label)
                : Loc.GetString("drydock-ui-modules-empty");
            var text = Loc.GetString("drydock-ui-modules-slot", ("slot", i + 1), ("module", label));
            if (key.Text != text)
                key.Text = text;

            key.Pressed = _openSlot == i;
            key.Disabled = state.ShipName == null;
        }

        if (_openSlot is not { } open)
            return;

        var openFitted = open < state.FittedModules.Count ? state.FittedModules[open] : null;
        _drawerSlot.Text = Loc.GetString("drydock-ui-modules-drawer-slot", ("slot", open + 1));
        _drawerSlot.FontColorOverride = Accent;
        _drawerRemove.Visible = openFitted != null && !state.Booking;
        _drawerRemove.Disabled = state.Working;

        var quote = ViewerQuote(state);
        var keyText = new StringBuilder();
        keyText.Append(open).Append('|').Append(string.Join(",", state.FittedModules.Select(m => m ?? "-")))
            .Append('|').Append(state.Booking).Append(state.Working).Append(blocked).Append(state.PaysWithMaterials);
        foreach (var entry in state.Modules)
        {
            keyText.Append('|').Append(entry.Id).Append(':')
                .Append(quote?.ModulePrices.GetValueOrDefault(entry.Id, entry.Price) ?? entry.Price);
            // Each bill row is red by its own shortage, so the key tracks every material, not just the whole bill.
            foreach (var (material, amount) in entry.Materials)
            {
                keyText.Append(':').Append(material).Append('=').Append(amount)
                    .Append(state.StoredMaterials.GetValueOrDefault(material) >= amount ? '+' : '-');
            }
        }

        var drawerKey = keyText.ToString();
        if (drawerKey == _drawerKey)
            return;

        _drawerKey = drawerKey;
        _moduleList.RemoveAllChildren();
        foreach (var entry in state.Modules)
        {
            if (_prototypeManager.TryIndex<WGShipModulePrototype>(entry.Id, out var module))
                _moduleList.AddChild(BuildModuleRow(entry, module, open, state, blocked));
        }

        Apply(_moduleList);
    }

    /// <summary>One module in the drawer: its icon, name, description, price and fit key.</summary>
    private Control BuildModuleRow(WGDrydockModuleEntry entry, WGShipModulePrototype module, int slot, WGDrydockConsoleState state,
        bool blocked)
    {
        var icon = new TextureRect
        {
            Texture = _entMan.System<SpriteSystem>().Frame0(module.Icon),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            TextureScale = new Vector2(1.5f, 1.5f),
            SetSize = new Vector2(ModuleIconSize, ModuleIconSize),
            VerticalAlignment = VAlignment.Top,
        };
        // A rich text title wraps a long name; it takes the theme's label font by hand.
        var name = new RichTextLabel
        {
            HorizontalExpand = true,
            Stylesheet = new Stylesheet(new StyleRule[] { Element<RichTextLabel>().Prop("font", _resCache.GetFont(Skin.MenuFonts, 12)) }),
        };
        name.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(module.Name)), defaultColor: Accent);
        var description = new RichTextLabel { HorizontalExpand = true };
        description.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(module.Description)), defaultColor: Muted);

        var bill = new WGDrydockReadout("drydock-ui-refit-cost") { EmptyText = Loc.GetString("drydock-console-materials-none") };
        if (state.PaysWithMaterials)
        {
            bill.SetRows(BillRows(entry.Materials, new Dictionary<string, int>(), state.StoredMaterials));
        }
        else
        {
            var price = ViewerQuote(state)?.ModulePrices.GetValueOrDefault(entry.Id, entry.Price) ?? entry.Price;
            bill.Value = Loc.GetString("drydock-ui-credits", ("price", price.ToString("N0", CultureInfo.InvariantCulture)));
        }

        var fitKey = new Button { HorizontalExpand = true };
        fitKey.AddStyleClass("WgCompact");
        var fittedAt = state.FittedModules.IndexOf(entry.Id);
        if (fittedAt == slot)
        {
            fitKey.Text = Loc.GetString("drydock-ui-modules-fitted");
            fitKey.Disabled = true;
            bill.Visible = false;
        }
        else if (fittedAt >= 0)
        {
            fitKey.Text = Loc.GetString("drydock-ui-modules-fitted-elsewhere", ("slot", fittedAt + 1));
            fitKey.Disabled = true;
            bill.Visible = false;
        }
        else if (slot < state.FittedModules.Count && state.FittedModules[slot] != null)
        {
            fitKey.Text = Loc.GetString("drydock-ui-modules-slot-taken", ("slot", slot + 1));
            fitKey.Disabled = true;
        }
        else if (state.Working)
        {
            fitKey.Text = Loc.GetString("drydock-ui-refit-busy");
            fitKey.Disabled = true;
        }
        else
        {
            fitKey.Text = Loc.GetString(state.Booking ? "drydock-ui-modules-book" : "drydock-ui-modules-fit", ("slot", slot + 1));
            fitKey.Disabled = blocked || (state.PaysWithMaterials && !Covers(entry.Materials, state.StoredMaterials));
            var key = WGShipModuleFitting.Key(slot, entry.Id);
            fitKey.OnPressed += _ => OnUpgradePressed?.Invoke(key);
        }

        var details = Column(name, Row(icon, description), bill, fitKey);
        details.SeparationOverride = 4;
        details.HorizontalExpand = true;
        details.Margin = new Thickness(4);

        // The theme frames a plain panel, boxing each module apart from the next.
        var card = new PanelContainer { HorizontalExpand = true };
        card.AddChild(details);
        return card;
    }
}
