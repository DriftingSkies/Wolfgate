using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.ShipShields;

/// <summary>
/// On a grid with a raised field: what a radar needs to draw it. Grids reach every client while the field itself only
/// reaches nearby ones, so this carries the outline to radars that can see the ship from farther off.
/// </summary>
[RegisterComponent, NetworkedComponent, UnsavedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class WGShipShieldRadarComponent : Component
{
    /// <summary>The field's outlines, as on <see cref="WGShipShieldVisualsComponent.Contours"/>.</summary>
    [AutoNetworkedField]
    public Vector2[][] Contours = Array.Empty<Vector2[]>();

    /// <summary>Remaining strength from zero to one, in coarse steps so hits rarely resend it.</summary>
    [AutoNetworkedField]
    public float Health = 1f;

    /// <summary>The generator's shield colour.</summary>
    [AutoNetworkedField]
    public Color Color = Color.White;
}
