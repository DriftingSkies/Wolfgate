using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WF.ShipAdmin;

/// <summary>
/// A small arrow pointing at a ship, as it lies from the viewer on screen. It draws a dot for the ship the viewer
/// is on and nothing for one on another map. Setting it costs no layout.
/// </summary>
public sealed class WFShipAdminCompass : Control
{
    private static readonly Color ArrowColour = Color.FromHex("#D9A441");
    private readonly List<Vector2> _arrow = new() { default, default, default };

    private Vector2? _direction;
    private bool _here;

    public WFShipAdminCompass()
    {
        MinSize = new Vector2(16, 16);
        VerticalAlignment = VAlignment.Center;
        MouseFilter = MouseFilterMode.Ignore;
    }

    /// <summary>Points the arrow along a screen direction, which need not be unit length.</summary>
    public void Point(Vector2 screenDirection)
    {
        var length = screenDirection.Length();
        _here = false;
        _direction = length > 0.001f ? screenDirection / length : null;
    }

    /// <summary>Shows the dot: the viewer is on this ship.</summary>
    public void Here()
    {
        _here = true;
        _direction = null;
    }

    /// <summary>Shows nothing: the ship is on another map, or there is nobody to point from.</summary>
    public void Clear()
    {
        _here = false;
        _direction = null;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var centre = PixelSize / 2f;
        var radius = MathF.Min(PixelSize.X, PixelSize.Y) / 2f;
        if (_here)
        {
            handle.DrawCircle(centre, radius * 0.4f, ArrowColour);
            return;
        }

        if (_direction is not { } along)
            return;

        var across = new Vector2(-along.Y, along.X);
        _arrow[0] = centre + along * radius;
        _arrow[1] = centre - along * radius * 0.7f + across * radius * 0.65f;
        _arrow[2] = centre - along * radius * 0.7f - across * radius * 0.65f;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _arrow, ArrowColour);
    }
}
