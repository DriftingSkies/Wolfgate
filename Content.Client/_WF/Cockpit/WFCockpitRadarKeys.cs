using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Cockpit;

/// <summary>
/// The radar's contact toggles on the scope glass: in the bottom row beside the mode buttons, or in a row above them
/// when the scope is too narrow for both.
/// </summary>
public sealed class WFCockpitRadarKeys : Control
{
    /// <summary>Distance kept from the scope's edge, the same as the mode buttons keep.</summary>
    public const float Edge = 8;

    /// <summary>Distance between the keys, and between them and the mode buttons.</summary>
    public const float Gap = 4;

    /// <summary>The size of one key.</summary>
    public static readonly Vector2 Key = new(52, 32);

    /// <summary>The box the mode buttons take at the scope's bottom left.</summary>
    public static readonly Vector2 Modes = new(140, 32);

    private readonly BoxContainer _row = new() { Name = "CockpitRadarKeyRow", SeparationOverride = (int) Gap };

    public WFCockpitRadarKeys(params Control[] keys)
    {
        Name = "CockpitRadarKeys";
        MouseFilter = MouseFilterMode.Ignore;
        foreach (var key in keys)
            _row.AddChild(key);
        AddChild(_row);
    }

    /// <summary>Where the keys sit on a scope of this size.</summary>
    public static UIBox2 Place(Vector2 scope, Vector2 keys)
    {
        var beside = scope.X >= Edge + Modes.X + Gap + keys.X + Edge;
        var position = beside
            ? new Vector2(scope.X - Edge - keys.X, scope.Y - Edge - keys.Y)
            : new Vector2(Edge, scope.Y - Edge - Modes.Y - Gap - keys.Y);
        return UIBox2.FromDimensions(position, keys);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _row.Measure(availableSize);
        // The keys ride on the scope and never ask it for room.
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _row.Arrange(Place(finalSize, _row.DesiredSize));
        return finalSize;
    }
}
