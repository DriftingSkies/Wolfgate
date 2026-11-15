using Robust.Shared.GameStates;

namespace Content.Shared._WF.AdminVessels;

/// <summary>
/// One door of an interlocked pair, such as a Lupercal core gate: it won't open unless its partner is closed. Unlike
/// bolting each other through device links, the pair can never hold each other open.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFDoorInterlockComponent : Component
{
    /// <summary>The other door of the pair.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Partner;
}
