using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WG.CharacterPreview;

/// <summary>
/// The character editor's preview box: drag across it to turn the character, scroll to zoom, and an optional floor
/// tile behind. It lays out the <see cref="SpriteView"/> it holds itself, so zooming past the box clips instead of
/// growing the pane.
/// </summary>
public sealed class WGPreviewFrame : Control
{
    /// <summary>Floor tiles to stand on, after "none" at index 0.</summary>
    public static readonly string[] Backgrounds =
    {
        "/Textures/Tiles/steel.png",
        "/Textures/Tiles/white.png",
        "/Textures/Tiles/dark.png",
        "/Textures/Tiles/plating.png",
        "/Textures/Tiles/wood.png",
        "/Textures/Tiles/lino.png",
        "/Textures/Tiles/cafeteria.png",
        "/Textures/Tiles/grass.png",
    };

    public const float MinZoom = 1f;
    public const float MaxZoom = 3f;
    public const float ZoomStep = 0.25f;

    /// <summary>How far the mouse moves across the box for one quarter turn, in UI units.</summary>
    public const float TurnDistance = 48f;

    private readonly TextureRect _background = new() { Stretch = TextureRect.StretchMode.Scale, MouseFilter = MouseFilterMode.Ignore };

    private SpriteView? _view;
    private Vector2? _baseScale;
    private Vector2? _baseSize;
    private bool _dragging;
    private float _dragAccumulated;
    private float _lastMouseX;

    /// <summary>Raised with +1 for a clockwise quarter turn, -1 for counter-clockwise.</summary>
    public event Action<int>? Turned;

    /// <summary>Current zoom, from <see cref="MinZoom"/> to <see cref="MaxZoom"/>.</summary>
    public float Zoom { get; private set; } = MinZoom;

    public WGPreviewFrame()
    {
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        AddChild(_background);
    }

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);
        if (newChild is SpriteView view)
        {
            _view = view;
            view.MouseFilter = MouseFilterMode.Ignore;
        }
    }

    /// <summary>Sets the floor tile by index into <see cref="Backgrounds"/> plus one; 0 or out of range is none.</summary>
    public void SetBackground(int index)
    {
        if (index <= 0 || index > Backgrounds.Length)
        {
            _background.Texture = null;
            _background.Visible = false;
            return;
        }

        var texture = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(Backgrounds[index - 1]).Texture;
        // Tile textures are strips of variants; show the first one.
        var size = Math.Min(texture.Width, texture.Height);
        _background.Texture = new AtlasTexture(texture, UIBox2.FromDimensions(0, 0, size, size));
        _background.Visible = true;
    }

    /// <summary>Sets the zoom, clamped to the allowed range.</summary>
    public void SetZoom(float zoom)
    {
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (_view != null)
        {
            // Fit never scales a sprite up, so zoom grows the view's size and scale together: Fit keeps its
            // unzoomed ratio and a wide sprite stays fitted, just larger.
            _baseScale ??= _view.Scale;
            _baseSize ??= _view.SetSize;
            _view.Scale = _baseScale.Value * Zoom;
            _view.SetSize = _baseSize.Value * Zoom;
        }

        InvalidateMeasure();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var desired = Vector2.Zero;
        foreach (var child in Children)
        {
            child.Measure(availableSize * Zoom);
            desired = Vector2.Max(desired, child.DesiredSize);
        }

        // Report the unzoomed size so zooming clips inside the box instead of growing the pane.
        return desired / Zoom;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var size = finalSize * Zoom;
        var rect = UIBox2.FromDimensions((finalSize - size) / 2, size);
        _view?.Arrange(rect);
        _background.Arrange(rect);
        return finalSize;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _dragging = true;
        _dragAccumulated = 0;
        _lastMouseX = args.RelativePosition.X;
        // Hold mouse focus so the drag keeps getting moves after the cursor leaves the box.
        UserInterfaceManager.ControlFocused = this;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _dragging = false;
        if (UserInterfaceManager.ControlFocused == this)
            UserInterfaceManager.ControlFocused = null;
    }

    protected override void ControlFocusExited()
    {
        base.ControlFocusExited();
        _dragging = false;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (!_dragging)
            return;

        var x = args.RelativePosition.X;
        _dragAccumulated += x - _lastMouseX;
        _lastMouseX = x;
        Drag(0);
    }

    /// <summary>Adds <paramref name="distance"/> of horizontal drag and raises a turn for each full step.</summary>
    public void Drag(float distance)
    {
        _dragAccumulated += distance;
        while (Math.Abs(_dragAccumulated) >= TurnDistance)
        {
            var step = Math.Sign(_dragAccumulated);
            _dragAccumulated -= step * TurnDistance;
            Turned?.Invoke(step);
        }
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        if (args.Delta.Y == 0)
            return;

        SetZoom(Zoom + Math.Sign(args.Delta.Y) * ZoomStep);
        args.Handle();
    }
}
