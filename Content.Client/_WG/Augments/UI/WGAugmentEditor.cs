using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client._WG.Stylesheets;
using Content.Client.Humanoid;
using Content.Shared._NF.Bank;
using Content.Shared._WG.Augments;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.Augments.UI;

/// <summary>
/// The creator's Augments tab: the character as a doll whose parts are clicked to pick their limb, organs and implants,
/// each a card with its icon and price, with the loadout and what confirming it costs or pays back beside it.
/// </summary>
public sealed partial class WGAugmentEditor : BoxContainer
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IEntityManager _entities = default!;

    /// <summary>Raised with the new slot choices and implants when the player changes one.</summary>
    public event Action<Dictionary<WGAugmentSlot, WGAugmentChoice>, HashSet<ProtoId<WGAugmentPrototype>>>? OnAugmentsChanged;

    /// <summary>Raised when the player confirms the purchase, which saves the profile.</summary>
    public event Action? OnConfirm;

    /// <summary>The width from which the tab has room for three columns.</summary>
    private const float WideWidth = 1030;
    private const float LoadoutIconSize = 28;
    private const float SideWidth = 310;

    private readonly WGAugmentTheme _theme = new();
    private readonly WGAugmentIcons _icons = new();
    private readonly BoxContainer _columns;
    private readonly PanelContainer _loadoutPanel;
    private readonly PanelContainer _billPanel;
    private readonly WGAugmentBackdrop _dollBlock;
    private readonly PanelContainer _partPanel;
    private readonly WGAugmentDoll _doll;
    private readonly Label _hint;
    private readonly Label _partTitle;
    private readonly ScrollContainer _partScroll;
    private readonly BoxContainer _part;
    private readonly BoxContainer _loadout;
    private readonly Label _buying;
    private readonly Control _refundRow;
    private readonly Label _refund;
    private readonly Label _total;
    private readonly Label _balance;
    private readonly Label _after;
    private readonly RichTextLabel _short;
    private readonly Button _confirm;

    private HumanoidCharacterProfile? _profile;
    private Dictionary<WGAugmentSlot, WGAugmentChoice> _choices = new();
    private HashSet<ProtoId<WGAugmentPrototype>> _implants = new();
    private Dictionary<WGAugmentSlot, ProtoId<TraitPrototype>> _claimed = new();
    private EntityUid _dollEntity;
    private string? _dollSpecies;
    private WGAugmentRegion? _shownRegion;
    private bool _wide;

    public WGAugmentEditor()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 8;

        AddChild(_theme.Wrapped(Loc.GetString("wg-augments-intro", ("percent", WGAugmentRules.RefundPercent)), _theme.Muted));
        _columns = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10, VerticalExpand = true };
        AddChild(_columns);

        var loadout = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        loadout.AddChild(Title(Loc.GetString("wg-augments-loadout")));
        loadout.AddChild(_theme.Rule());
        _loadout = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        loadout.AddChild(new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _loadout } });
        _loadoutPanel = Panel(loadout);
        _loadoutPanel.VerticalExpand = true;
        _loadoutPanel.SizeFlagsStretchRatio = 1;

        var bill = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 3 };
        bill.AddChild(BillRow("wg-augments-bill-buying", _theme.Price, out _buying));
        bill.AddChild(_refundRow = BillRow("wg-augments-bill-refund", _theme.Good, out _refund));
        bill.AddChild(_theme.Rule());
        bill.AddChild(BillRow("wg-augments-bill-total", _theme.Text, out _total, true));
        bill.AddChild(BillRow("wg-augments-bill-balance", _theme.Muted, out _balance));
        bill.AddChild(BillRow("wg-augments-bill-after", _theme.Muted, out _after));
        _short = _theme.Wrapped(Loc.GetString("wg-augments-short"), _theme.Bad);
        bill.AddChild(_short);
        _confirm = new Button
        {
            Text = Loc.GetString("wg-augments-confirm"),
            Margin = new Thickness(0, 6, 0, 0),
            MinHeight = 36,
            StyleClasses = { StyleWolfgate.StyleClassCreatorPrimary },
        };
        _confirm.OnPressed += _ => OnConfirm?.Invoke();
        bill.AddChild(_confirm);
        _billPanel = Panel(bill);

        // The plate fills its column, the doll in its middle and the hovered part's name along its foot.
        _doll = new WGAugmentDoll
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(10, 10, 10, 30),
            HoverColor = _theme.Text,
            SelectedColor = _theme.Accent,
        };
        _doll.OnRegionHovered += _ => UpdateHint();
        _doll.OnRegionPressed += _ => RebuildPart();
        _hint = _theme.Label(string.Empty, _theme.Muted, _theme.Heading);
        _hint.HorizontalAlignment = HAlignment.Center;
        _hint.VerticalAlignment = VAlignment.Bottom;
        _hint.Margin = new Thickness(0, 0, 0, 8);
        _dollBlock = new WGAugmentBackdrop(_theme) { VerticalExpand = true, Children = { _doll, _hint } };

        var part = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        _partTitle = Title(string.Empty);
        part.AddChild(_partTitle);
        part.AddChild(_theme.Rule());
        _part = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        _partScroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false, Children = { _part } };
        part.AddChild(_partScroll);
        _partPanel = Panel(part);
        _partPanel.HorizontalExpand = true;
        _partPanel.VerticalExpand = true;
        _partPanel.SizeFlagsStretchRatio = 2;

        Lay(true);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (Width > 0 && Width >= WideWidth != _wide)
            Lay(!_wide);
    }

    /// <summary>
    /// Places the blocks: the loadout and bill, the doll, then the part where there is room for three columns; else
    /// the doll over the bill beside the part over the loadout.
    /// </summary>
    private void Lay(bool wide)
    {
        _wide = wide;
        _loadoutPanel.Orphan();
        _billPanel.Orphan();
        _dollBlock.Orphan();
        _partPanel.Orphan();
        _columns.RemoveAllChildren();

        if (wide)
        {
            _columns.AddChild(Stack(_loadoutPanel, _billPanel, SideWidth));
            _columns.AddChild(_dollBlock);
            _columns.AddChild(_partPanel);
        }
        else
        {
            _columns.AddChild(Stack(_dollBlock, _billPanel, null));
            _columns.AddChild(Stack(_partPanel, _loadoutPanel, null));
        }
    }

    /// <summary>Two blocks, one over the other; with no width given the column takes the room left over.</summary>
    private static BoxContainer Stack(Control top, Control bottom, float? width)
    {
        var stack = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 10, Children = { top, bottom } };
        if (width is { } set)
            stack.SetWidth = set;
        else
            stack.HorizontalExpand = top.HorizontalExpand;
        return stack;
    }

    /// <summary>One of the tab's framed blocks, in the creator's card.</summary>
    private static PanelContainer Panel(Control content)
    {
        return new PanelContainer { StyleClasses = { StyleWolfgate.StyleClassCreatorCard }, Children = { content } };
    }

    private static Label Title(string text)
    {
        return new Label { Text = text, StyleClasses = { StyleWolfgate.StyleClassCreatorCardTitle } };
    }

    /// <summary>Shows a profile: its doll, its loadout and the selected part's choices.</summary>
    public void SetProfile(HumanoidCharacterProfile profile)
    {
        _profile = profile;
        _choices = new Dictionary<WGAugmentSlot, WGAugmentChoice>(profile.Augments);
        _implants = new HashSet<ProtoId<WGAugmentPrototype>>(profile.AugmentImplants);
        _claimed = WGAugmentRules.ClaimedSlots(profile.TraitPreferences, _proto);

        UpdateDoll();
        RebuildLoadout();
        RebuildPart();
        UpdateHint();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        UpdateDoll();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        DeleteDoll();
    }

    /// <summary>Spawns the species' doll if it changed, and dresses it in the profile with no clothes on.</summary>
    private void UpdateDoll()
    {
        if (_profile == null || !IsInsideTree)
            return;

        string species = _proto.HasIndex<SpeciesPrototype>(_profile.Species)
            ? (string) _profile.Species
            : (string) SharedHumanoidAppearanceSystem.DefaultSpecies;
        if (_dollSpecies != species || !_entities.EntityExists(_dollEntity))
        {
            DeleteDoll();
            _dollEntity = _entities.SpawnEntity(_proto.Index<SpeciesPrototype>(species).DollPrototype, MapCoordinates.Nullspace);
            _dollSpecies = species;
            _doll.SetEntity(_dollEntity);
        }

        _entities.System<HumanoidAppearanceSystem>().LoadProfile(_dollEntity, _profile);
    }

    private void DeleteDoll()
    {
        _doll.SetEntity(null);
        if (_dollEntity.IsValid())
            _entities.DeleteEntity(_dollEntity);
        _dollEntity = EntityUid.Invalid;
        _dollSpecies = null;
    }

    private void UpdateHint()
    {
        var region = _doll.Hovered ?? _doll.Selected;
        _hint.Text = region is { } shown ? RegionName(shown) : Loc.GetString("wg-augments-pick");
    }

    private void Changed()
    {
        OnAugmentsChanged?.Invoke(new Dictionary<WGAugmentSlot, WGAugmentChoice>(_choices),
            new HashSet<ProtoId<WGAugmentPrototype>>(_implants));
    }

    #region Loadout

    private void RebuildLoadout()
    {
        _loadout.RemoveAllChildren();
        if (_profile == null)
            return;

        var any = false;
        foreach (var (slot, choice) in _choices.OrderBy(c => WGAugmentRegions.Of(c.Key)).ThenBy(c => c.Key))
        {
            if (_proto.TryIndex(choice.Augment, out var augment))
            {
                _loadout.AddChild(LoadoutRow(slot, augment));
                any = true;
            }
        }

        foreach (var id in _implants.OrderBy(i => i.Id))
        {
            if (_proto.TryIndex(id, out var implant))
            {
                _loadout.AddChild(LoadoutRow(implant.Slot, implant));
                any = true;
            }
        }

        var given = WGAugmentRules.Sold(_profile, _proto);
        foreach (var sold in given)
        {
            // One kept from a round was never paid for, so it is scrapped for nothing.
            var back = WGAugmentRules.Refund(_profile, sold);
            _loadout.AddChild(new PanelContainer
            {
                PanelOverride = _theme.Row,
                Children =
                {
                    LoadoutEntry(_icons.Of(sold),
                        true,
                        Loc.GetString(back > 0 ? "wg-augments-sold" : "wg-augments-scrapped", ("name", Named(sold))),
                        SlotName(sold.Slot),
                        back > 0 ? "+" + BankSystemExtensions.ToSpesoString(back) : Loc.GetString("wg-augments-no-refund"),
                        back > 0 ? _theme.Good : _theme.Bad),
                },
            });
            any = true;
        }

        if (!any)
            _loadout.AddChild(_theme.Label(Loc.GetString("wg-augments-loadout-empty"), _theme.Faint));

        var due = WGAugmentRules.Due(_profile, _proto);
        var refund = given.Sum(sold => WGAugmentRules.Refund(_profile, sold));
        var net = due - refund;
        var after = _profile.BankBalance - net;
        _buying.Text = BankSystemExtensions.ToSpesoString(due);
        _refund.Text = Negative(refund);
        _refundRow.Visible = refund > 0;
        _total.Text = Signed(net);
        _total.FontColorOverride = net > 0 ? _theme.Price : net < 0 ? _theme.Good : _theme.Text;
        _balance.Text = BankSystemExtensions.ToSpesoString(_profile.BankBalance);
        _after.Text = Signed(after);
        _after.FontColorOverride = after < 0 ? _theme.Bad : _theme.Muted;
        _short.Visible = net > _profile.BankBalance;
        _confirm.Disabled = due == 0 && given.Count == 0;
    }

    /// <summary>An augment in the loadout; clicking it opens its part.</summary>
    private Control LoadoutRow(WGAugmentSlot slot, WGAugmentPrototype augment)
    {
        var (price, color) = PriceText(augment);
        var row = new WGAugmentRowButton(_theme.Row, _theme.RowHover, _theme.RowHover, _theme.Row);
        row.AddChild(LoadoutEntry(_icons.Of(augment), false, Named(augment), SlotName(slot), price, color));
        row.OnPressed += _ =>
        {
            _doll.Selected = WGAugmentRegions.Of(slot);
            RebuildPart();
        };
        return row;
    }

    private Control LoadoutEntry(IReadOnlyList<WGAugmentIconLayer> icon, bool gone, string name, string where, string amount, Color amountColor)
    {
        var text = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        text.AddChild(_theme.WrappedName(name, gone ? _theme.Muted : _theme.Text, false));

        // Where it sits and what it costs share the second line, so the name has the whole first.
        var second = new BoxContainer { Orientation = LayoutOrientation.Horizontal, MouseFilter = MouseFilterMode.Ignore };
        var slot = _theme.Label(where, _theme.Faint, small: true);
        slot.HorizontalExpand = true;
        slot.VerticalAlignment = VAlignment.Center;
        second.AddChild(slot);
        second.AddChild(_theme.Label(amount, amountColor, _theme.Amount));
        text.AddChild(second);
        return new WGAugmentEntry(_theme.Icon(icon, LoadoutIconSize, gone), text);
    }

    /// <summary>A line of the bill: what it is on the left, the amount on the right.</summary>
    private Control BillRow(string label, Color color, out Label amount, bool total = false)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        var name = _theme.Label(Loc.GetString(label), total ? _theme.Text : _theme.Muted, total ? _theme.Name : null);
        name.HorizontalExpand = true;
        name.VerticalAlignment = VAlignment.Center;
        row.AddChild(name);
        amount = _theme.Label(string.Empty, color, total ? _theme.Total : _theme.Amount);
        row.AddChild(amount);
        return row;
    }

    #endregion

    #region Part

    /// <summary>Lists the selected region's choices: each slot's options, a limb's style, and the part's implants.</summary>
    private void RebuildPart()
    {
        _part.RemoveAllChildren();
        UpdateHint();
        if (_shownRegion != _doll.Selected)
        {
            _shownRegion = _doll.Selected;
            _partScroll.SetScrollValue(Vector2.Zero);
        }

        if (_profile == null || _doll.Selected is not { } region)
        {
            _partTitle.Text = Loc.GetString("wg-augments-cyberware");
            _part.AddChild(_theme.Label(Loc.GetString("wg-augments-pick"), _theme.Faint));
            return;
        }

        _partTitle.Text = RegionName(region);
        var any = false;
        var slots = WGAugmentRegions.Slots(region);
        foreach (var slot in slots)
        {
            var claimed = _claimed.TryGetValue(slot, out var trait);
            var augments = WGAugmentRules.AugmentsFor(_profile.Species, slot, _proto, _factory, _profile.Biotype);
            if (!claimed && augments.Count == 0)
                continue;

            any = true;
            // A region of one slot already has the slot's name as its title.
            if (slots.Length > 1)
                _part.AddChild(_theme.Section(SlotName(slot)));

            if (claimed)
            {
                var name = _proto.TryIndex(trait, out var traitProto) ? Loc.GetString(traitProto.Name) : trait.Id;
                _part.AddChild(_theme.Label(Loc.GetString("wg-augments-claimed", ("trait", name)), _theme.Faint));
                continue;
            }

            AddSlotOptions(slot, augments);
        }

        var host = WGAugmentRegions.Host(region);
        var implants = WGAugmentRules.ImplantsFor(_profile.Species, host, _proto, _factory, _profile.Biotype);
        if (implants.Count > 0)
        {
            any = true;
            AddImplants(host, implants);
        }

        if (!any)
            _part.AddChild(_theme.Label(Loc.GetString("wg-augments-nothing"), _theme.Faint));
    }

    private void AddSlotOptions(WGAugmentSlot slot, List<WGAugmentPrototype> augments)
    {
        _choices.TryGetValue(slot, out var current);
        var picked = augments.FirstOrDefault(a => a.ID == current.Augment.Id);

        var natural = new WGAugmentCardInfo(_icons.Natural(_profile!, slot), Loc.GetString("wg-augments-none"), null, null, string.Empty, _theme.Muted);
        _part.AddChild(WGAugmentCard.Create(_theme, natural, picked == null, false, () =>
        {
            _choices.Remove(slot);
            Changed();
        }));

        foreach (var augment in augments)
        {
            var (price, color) = PriceText(augment);
            var info = new WGAugmentCardInfo(_icons.Of(augment),
                Named(augment),
                WGAugmentRules.Description(augment, _proto),
                null,
                price,
                color);
            _part.AddChild(WGAugmentCard.Create(_theme, info, augment == picked, false, () =>
            {
                _choices[slot] = new WGAugmentChoice(augment.ID, current.Style);
                Changed();
            }));
        }

        if (picked is not { Styled: true })
            return;

        var styles = WGAugmentRules.StylesFor(slot, _proto);
        var entries = new List<(IReadOnlyList<WGAugmentIconLayer> Icon, string Name)>();
        foreach (var style in styles)
        {
            entries.Add((_icons.Of(style, slot), Loc.GetString(style.Name)));
        }

        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8, Margin = new Thickness(0, 2, 0, 0) };
        var label = _theme.Label(Loc.GetString("wg-augments-style"), _theme.Muted, _theme.Heading);
        label.VerticalAlignment = VAlignment.Center;
        row.AddChild(label);
        var picker = new WGAugmentStylePicker(_theme, entries, styles.FindIndex(s => s.ID == current.Style?.Id));
        picker.OnPicked += index =>
        {
            _choices[slot] = new WGAugmentChoice(picked.ID, styles[index].ID);
            Changed();
        };
        row.AddChild(picker);
        _part.AddChild(row);
    }

    /// <summary>The part's implants, each toggled on or off, greyed when the part can't take it.</summary>
    private void AddImplants(WGAugmentSlot host, List<WGAugmentPrototype> implants)
    {
        var slots = WGAugmentRules.ImplantSlots(host, _choices, _claimed, _proto);
        var synthetic = WGAugmentRules.Synthetic(_profile!.Species, host, _choices, _claimed, _proto, _factory, _profile.Biotype);
        var used = implants.Count(i => _implants.Contains(i.ID));
        _part.AddChild(_theme.Section(Loc.GetString("wg-augments-implants", ("used", used), ("slots", slots)), new Pips(used, slots, _theme.Accent, _theme.Faint)));

        foreach (var implant in implants)
        {
            var installed = _implants.Contains(implant.ID);
            string? blocked = null;
            if (!WGAugmentRules.HostFits(implant, synthetic))
                blocked = Loc.GetString(implant.Host == WGAugmentHost.Synthetic ? "wg-augments-needs-synthetic" : "wg-augments-needs-organic");
            else if (!installed && used >= slots)
                blocked = Loc.GetString("wg-augments-no-room");
            else if (!installed && ExclusiveTaken(implant))
                blocked = Loc.GetString("wg-augments-exclusive");

            var (price, color) = PriceText(implant);
            var info = new WGAugmentCardInfo(_icons.Of(implant),
                Named(implant),
                WGAugmentRules.Description(implant, _proto),
                blocked,
                price,
                color);
            _part.AddChild(WGAugmentCard.Create(_theme, info, installed, blocked != null && !installed, () =>
            {
                if (!_implants.Remove(implant.ID))
                    _implants.Add(implant.ID);
                Changed();
            }));
        }
    }

    /// <summary>Whether another implant of the same exclusive group is already fitted anywhere on the body.</summary>
    private bool ExclusiveTaken(WGAugmentPrototype implant)
    {
        return implant.Exclusive is { } group
            && _implants.Any(id => id != implant.ID && _proto.TryIndex(id, out var other) && other.Exclusive == group);
    }

    /// <summary>A square for each implant slot of a part, filled for the ones in use.</summary>
    private sealed class Pips : Control
    {
        private const float Pip = 8;
        private const float Gap = 3;

        private readonly int _used;
        private readonly int _slots;
        private readonly Color _full;
        private readonly Color _empty;

        public Pips(int used, int slots, Color full, Color empty)
        {
            _full = full;
            _empty = empty;
            _used = used;
            _slots = Math.Max(slots, 0);
            SetSize = new Vector2(Math.Max(_slots * (Pip + Gap) - Gap, 0), Pip);
            VerticalAlignment = VAlignment.Center;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            for (var i = 0; i < _slots; i++)
            {
                var box = UIBox2.FromDimensions(i * (Pip + Gap) * UIScale, 0, Pip * UIScale, Pip * UIScale);
                if (i < _used)
                    handle.DrawRect(box, _full);
                else
                    handle.DrawRect(box, _empty, filled: false);
            }
        }
    }

    #endregion

    #region Text

    /// <summary>What an augment costs this character: free, owned, or its price.</summary>
    private (string Text, Color Color) PriceText(WGAugmentPrototype augment)
    {
        if (augment.Price <= 0)
            return (Loc.GetString("wg-augments-free"), _theme.Muted);
        if (_profile != null && _profile.AugmentUnlocks.Contains(augment.ID))
            return (Loc.GetString("wg-augments-owned"), _theme.Good);
        return (BankSystemExtensions.ToSpesoString(augment.Price), _theme.Price);
    }

    /// <summary>The augment's name with a capital; one taken from its entity starts in lower case.</summary>
    private string Named(WGAugmentPrototype augment)
    {
        var name = WGAugmentRules.Name(augment, _proto);
        return name.Length == 0 ? name : name[..1].ToUpperInvariant() + name[1..];
    }

    private static string Negative(int amount)
    {
        return "-" + BankSystemExtensions.ToSpesoString(amount);
    }

    private static string Signed(int amount)
    {
        return amount < 0 ? Negative(-amount) : BankSystemExtensions.ToSpesoString(amount);
    }

    private static string RegionName(WGAugmentRegion region)
    {
        return region == WGAugmentRegion.Torso
            ? Loc.GetString("wg-augments-torso")
            : SlotName(WGAugmentRegions.Host(region));
    }

    /// <summary>The slot's name as players read it.</summary>
    internal static string SlotName(WGAugmentSlot slot)
    {
        return Loc.GetString("wg-augment-slot-" + SlotKey(slot));
    }

    /// <summary>The slot's name in kebab case, as its Fluent key ends: LeftHand is left-hand.</summary>
    private static string SlotKey(WGAugmentSlot slot)
    {
        var name = slot.ToString();
        var key = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                key.Append('-');
            key.Append(char.ToLowerInvariant(name[i]));
        }

        return key.ToString();
    }

    #endregion
}
