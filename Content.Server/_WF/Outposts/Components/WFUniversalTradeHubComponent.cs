namespace Content.Server._WF.Outposts.Components;

/// <summary>
/// An outpost's trade hub: what the owner orders on the console's Trade tab comes down on it by parachute. It takes
/// orders while it has power and open sky over it.
/// </summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFUniversalTradeHubComponent : Component
{
    /// <summary>How far from the hub, in tiles, a drop may come down when the hub itself is covered.</summary>
    [DataField]
    public float DropRadius = 5f;

    /// <summary>
    /// A hub off any outpost, such as a Tradepost's: it takes the orders of every outpost on its world whose own hubs
    /// can't take them.
    /// </summary>
    [DataField]
    public bool Public;
}
