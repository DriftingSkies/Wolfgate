using Robust.Shared.Map;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>
/// Standing orders for a guard who would otherwise wander the deck: walk these points in turn, or with none stay at the
/// post.
/// </summary>
[RegisterComponent]
public sealed partial class WGCrewPatrolOrdersComponent : Component
{
    /// <summary>The points he walks in turn; empty keeps him at his post.</summary>
    [DataField]
    public List<EntityCoordinates> Route = new();

    /// <summary>The next point on the route.</summary>
    [ViewVariables]
    public int Next;
}
