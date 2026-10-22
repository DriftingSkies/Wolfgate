using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WF.MappingMode.QuickSelect;

/// <summary>
/// One slot of the quick-select bar: the entry's icon and the slot number. A click places it, a right click opens its
/// menu, and dragging it onto another slot swaps the two.
/// </summary>
public sealed class WFMappingQuickSlot : PanelContainer
{
    private const float SlotSize = 44f;

    /// <summary>How far, in UI pixels, the mouse must move before a press counts as a drag.</summary>
    private const float DragDeadzone = 6f;

    private static readonly Color Background = Color.FromHex("#1b1b1e");
    private static readonly Color Border = Color.FromHex("#4a4a55");
    private static readonly Color SelectedBorder = Color.FromHex("#4fc3f7");
    private static readonly Color MissingBorder = Color.FromHex("#a03030");

    private readonly StyleBoxFlat _style;
    private readonly TextureRect _icon;
    private readonly Label _label;
    private Vector2? _pressedAt;

    public readonly int Index;

    /// <summary>Whether the slot holds an entry.</summary>
    public bool Filled { get; private set; }

    /// <summary>Raised on a click that wasn't a drag.</summary>
    public event Action<WFMappingQuickSlot>? Clicked;

    public event Action<WFMappingQuickSlot>? RightClicked;

    /// <summary>Raised with this slot and the slot it was dropped on.</summary>
    public event Action<WFMappingQuickSlot, WFMappingQuickSlot>? Dropped;

    public WFMappingQuickSlot(int index)
    {
        Index = index;
        MouseFilter = MouseFilterMode.Stop;
        SetSize = new Vector2(SlotSize, SlotSize);
        TooltipDelay = 0.3f;

        _style = new StyleBoxFlat
        {
            BackgroundColor = Background,
            BorderColor = Border,
            BorderThickness = new Thickness(1),
        };
        PanelOverride = _style;

        _icon = new TextureRect
        {
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            SetSize = new Vector2(32, 32),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(_icon);

        _label = new Label
        {
            Text = Loc.GetString("wf-mapping-quickselect-slot-number", ("number", index + 1)),
            StyleClasses = { "LabelSubText" },
            HorizontalAlignment = HAlignment.Left,
            VerticalAlignment = VAlignment.Top,
            Margin = new Thickness(3, 0, 0, 0),
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(_label);
    }

    /// <summary>Shows a slot's icon, tooltip and border: empty, unknown id, selected or plain.</summary>
    public void Set(Texture? icon, string? tooltip, bool filled, bool known, bool selected)
    {
        Filled = filled;
        _icon.Texture = icon;
        _icon.Modulate = known ? Color.White : Color.White.WithAlpha(0.35f);
        ToolTip = tooltip;
        _style.BorderColor = !filled ? Border : !known ? MissingBorder : selected ? SelectedBorder : Border;
        _style.BorderThickness = new Thickness(selected ? 2 : 1);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            _pressedAt = args.PointerLocation.Position;
            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            _pressedAt = null;
            RightClicked?.Invoke(this);
            args.Handle();
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick || _pressedAt is not { } pressedAt)
            return;

        _pressedAt = null;
        args.Handle();

        var moved = (args.PointerLocation.Position - pressedAt).Length() / UIScale >= DragDeadzone;
        var target = SlotAt(UserInterfaceManager.MouseGetControl(args.PointerLocation));
        if (!moved || target == this)
        {
            if (target == this)
                Clicked?.Invoke(this);

            return;
        }

        if (target != null)
            Dropped?.Invoke(this, target);
    }

    private static WFMappingQuickSlot? SlotAt(Control? control)
    {
        for (var current = control; current != null; current = current.Parent)
        {
            if (current is WFMappingQuickSlot slot)
                return slot;
        }

        return null;
    }
}
