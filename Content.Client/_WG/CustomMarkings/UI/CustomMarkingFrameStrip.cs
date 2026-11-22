using Content.Client._WG.Stylesheets;
using Content.Shared._WG.CustomMarkings;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._WG.CustomMarkings.UI;

/// <summary>
/// A marking's frames as a row of small previews with their times: clicking one picks it to draw on, and dragging
/// it along the row moves it there.
/// </summary>
public sealed class CustomMarkingFrameStrip : BoxContainer
{
    public const int PreviewScale = 2;

    private readonly List<(ContainerButton Tile, CustomMarkingCanvas Preview, Label Time)> _tiles = new();

    /// <summary>The frame being dragged, where it is now.</summary>
    private int? _dragging;

    private bool _moved;

    /// <summary>A frame was clicked.</summary>
    public event Action<int>? FramePicked;

    /// <summary>A frame was first dragged off its place.</summary>
    public event Action? DragStarted;

    /// <summary>The dragged frame goes from one place to another.</summary>
    public event Action<int, int>? FrameMoved;

    /// <summary>A frame that was moved was let go.</summary>
    public event Action? DragEnded;

    /// <summary>Sets up a preview made for a new frame like the window's other previews.</summary>
    public Action<CustomMarkingCanvas>? SetUpPreview;

    public CustomMarkingFrameStrip()
    {
        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        MouseFilter = MouseFilterMode.Stop;
    }

    public IEnumerable<CustomMarkingCanvas> Previews
    {
        get
        {
            foreach (var tile in _tiles)
            {
                yield return tile.Preview;
            }
        }
    }

    /// <summary>Shows a preview of each frame of the art on a facing, with the one drawn on picked out.</summary>
    public void Refresh(CustomMarkingArt art, int facing, int picked)
    {
        while (_tiles.Count < art.Frames)
        {
            AddTile();
        }

        while (_tiles.Count > art.Frames)
        {
            _tiles[^1].Tile.Orphan();
            _tiles.RemoveAt(_tiles.Count - 1);
        }

        for (var i = 0; i < _tiles.Count; i++)
        {
            var (tile, preview, time) = _tiles[i];
            preview.Art = art;
            preview.ArtFrame = i;
            preview.Facing = facing;
            tile.Pressed = i == picked;
            time.Text = Loc.GetString("wg-custom-marking-editor-frame-seconds", ("seconds", art.GetFrameTime(i) / 1000f));
        }
    }

    private void AddTile()
    {
        var preview = new CustomMarkingCanvas(PreviewScale, false);
        SetUpPreview?.Invoke(preview);
        var time = new Label
        {
            Align = Label.AlignMode.Center,
            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
        };

        // The strip takes the clicks, so a frame can be dragged from anywhere on it.
        var tile = new ContainerButton
        {
            StyleClasses = { ContainerButton.StyleClassButton, StyleWolfgate.StyleClassCreatorToggle },
            ToggleMode = true,
            MouseFilter = MouseFilterMode.Ignore,
            Children =
            {
                new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    SeparationOverride = 2,
                    Children = { preview, time },
                },
            },
        };

        _tiles.Add((tile, preview, time));
        AddChild(tile);
    }

    /// <summary>Which frame's tile is at a point along the strip, or the nearest end when the point is past one.</summary>
    private int? FrameAt(float x, bool nearest)
    {
        if (_tiles.Count == 0)
            return null;

        if (x < 0)
            return nearest ? 0 : null;

        for (var i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i].Tile;
            if (x < tile.Position.X + tile.Size.X + (SeparationOverride ?? 0) / 2f)
                return i;
        }

        return nearest ? _tiles.Count - 1 : null;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || FrameAt(args.RelativePosition.X, false) is not { } frame)
            return;

        _dragging = frame;
        _moved = false;
        FramePicked?.Invoke(frame);
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_dragging is not { } from || FrameAt(args.RelativePosition.X, true) is not { } to || to == from)
            return;

        if (!_moved)
        {
            _moved = true;
            DragStarted?.Invoke();
        }

        FrameMoved?.Invoke(from, to);
        _dragging = to;
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick || _dragging == null)
            return;

        _dragging = null;
        if (_moved)
            DragEnded?.Invoke();
    }
}
