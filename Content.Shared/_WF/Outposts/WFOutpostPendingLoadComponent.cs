using Robust.Shared.GameStates;

namespace Content.Shared._WF.Outposts;

/// <summary>
/// On an outpost a saved outpost is about to replace: the tiles it will cover, in this outpost's own tile coordinates,
/// drawn on the ground until it lands.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WFOutpostPendingLoadComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public List<Vector2i> Tiles = new();

    [ViewVariables, AutoNetworkedField]
    public TimeSpan Lands;
}
