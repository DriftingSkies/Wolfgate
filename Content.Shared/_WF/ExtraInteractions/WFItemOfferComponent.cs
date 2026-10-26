using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ExtraInteractions;

/// <summary>
/// On a mob holding out an item for anyone nearby to take.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(WFItemOfferSystem))]
public sealed partial class WFItemOfferComponent : Component
{
    /// <summary>The held item on offer.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Item;
}

/// <summary>
/// Raised by a client that clicked the offered item's icon, to take it.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFTakeOfferedItemEvent(NetEntity offerer) : EntityEventArgs
{
    public NetEntity Offerer = offerer;
}
