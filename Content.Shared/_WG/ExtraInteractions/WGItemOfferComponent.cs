using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ExtraInteractions;

/// <summary>
/// On a mob holding out an item for anyone nearby to take.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(WGItemOfferSystem))]
public sealed partial class WGItemOfferComponent : Component
{
    /// <summary>The held item on offer.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Item;
}

/// <summary>
/// Raised by a client that clicked the offered item's icon, to take it.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGTakeOfferedItemEvent(NetEntity offerer) : EntityEventArgs
{
    public NetEntity Offerer = offerer;
}
