using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.GridMomentum;

/// <summary>The grid a mob last stood on, and the velocity it carried away when it floated off.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGGridMomentumComponent : Component
{
    /// <summary>The grid the mob is on, or was on until it floated off this tick.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Grid;

    /// <summary>The grid the mob floated off and is still beside.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Left;

    /// <summary>The map velocity of the grid under the mob as it left; beside that grid, it slows and pushes off relative to this.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 Frame;
}
