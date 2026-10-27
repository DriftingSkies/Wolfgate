using Content.Shared.Item;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ExtraInteractions;

/// <summary>
/// Pointing at an item you carry shows it in a thought bubble over you, as in SS13, instead of pointing at the floor under the HUD.
/// </summary>
public abstract class SharedWFShowItemSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How long the bubble stays up.</summary>
    public static readonly TimeSpan BubbleDuration = TimeSpan.FromSeconds(2.5);

    /// <summary>Whether the item is in the user's hands, inventory or anything they carry.</summary>
    public bool IsCarried(EntityUid user, EntityUid item)
    {
        return item != user && HasComp<ItemComponent>(item) && _transform.IsParentOf(Transform(user), item);
    }
}

/// <summary>
/// Raised by a client that pointed at an item it carries in the hands, inventory or storage UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFShowItemRequestEvent(NetEntity item) : EntityEventArgs
{
    public NetEntity Item = item;
}

/// <summary>
/// Sent to clients near someone showing an item, to draw it in a bubble over them.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFShowItemEvent(NetEntity user, NetEntity item, EntProtoId? prototype) : EntityEventArgs
{
    public NetEntity User = user;
    public NetEntity Item = item;

    /// <summary>Drawn instead when the client doesn't have the item, such as one deep in a bag.</summary>
    public EntProtoId? Prototype = prototype;
}
