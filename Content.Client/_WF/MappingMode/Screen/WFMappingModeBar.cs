using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WF.MappingMode.Screen;

/// <summary>
/// The mode buttons on a bar that can be dragged around the viewport by the grab handle at its start, lying flat or
/// standing upright. <see cref="WFMappingScreen"/> places it; this only reports where the handle was dragged to.
/// </summary>
public sealed class WFMappingModeBar : PanelContainer
{
    private const float HandleThickness = 12f;

    private readonly BoxContainer _box;
    private readonly GrabHandle _handle;
    private bool _vertical;

    /// <summary>Where the mode buttons go.</summary>
    public readonly BoxContainer Buttons;

    /// <summary>Raised while dragging, with where the bar's top-left would be in global UI units.</summary>
    public event Action<Vector2>? Dragged;

    /// <summary>Raised when the handle is let go.</summary>
    public event Action? DragEnded;

    public bool Dragging => _handle.Dragging;

    public WFMappingModeBar()
    {
        PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#16161ACC") };

        _box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };
        _handle = new GrabHandle(this)
        {
            ToolTip = Loc.GetString("wf-mapping-modebar-handle-tooltip"),
            TooltipDelay = 0.3f,
        };
        Buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 2 };

        _box.AddChild(_handle);
        _box.AddChild(Buttons);
        AddChild(_box);
        SetHandleSize();
    }

    /// <summary>Whether the bar stands upright, the handle on top.</summary>
    public bool Vertical
    {
        get => _vertical;
        set
        {
            if (_vertical == value)
                return;

            _vertical = value;
            var orientation = value ? BoxContainer.LayoutOrientation.Vertical : BoxContainer.LayoutOrientation.Horizontal;
            _box.Orientation = orientation;
            Buttons.Orientation = orientation;
            SetHandleSize();

            // A child's orientation doesn't invalidate its parents until it is measured again.
            InvalidateMeasure();
        }
    }

    private void SetHandleSize()
    {
        _handle.MinSize = _vertical ? new Vector2(0, HandleThickness) : new Vector2(HandleThickness, 0);
    }

    /// <summary>The bar's size in either orientation, measured without changing it.</summary>
    public Vector2 SizeFor(bool vertical)
    {
        var old = Vertical;
        Vertical = vertical;
        Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        var size = DesiredSize;
        Vertical = old;
        if (old != vertical)
            Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));

        return size;
    }

    /// <summary>The strip at the bar's start that drags it.</summary>
    private sealed class GrabHandle : PanelContainer
    {
        private readonly WFMappingModeBar _bar;

        /// <summary>Where the pointer took hold, from the bar's top-left.</summary>
        private Vector2 _grab;

        public bool Dragging { get; private set; }

        public GrabHandle(WFMappingModeBar bar)
        {
            _bar = bar;
            MouseFilter = MouseFilterMode.Stop;
            DefaultCursorShape = CursorShape.Move;
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#3A3A44"),
                BorderColor = Color.FromHex("#5A5A66"),
                BorderThickness = new Thickness(1),
            };
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            Dragging = true;
            _grab = GlobalPosition + args.RelativePosition - _bar.GlobalPosition;
            args.Handle();
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);
            if (args.Function != EngineKeyFunctions.UIClick || !Dragging)
                return;

            Dragging = false;
            args.Handle();
            _bar.DragEnded?.Invoke();
        }

        protected override void MouseMove(GUIMouseMoveEventArgs args)
        {
            base.MouseMove(args);
            if (Dragging)
                _bar.Dragged?.Invoke(args.GlobalPosition - _grab);
        }
    }
}
