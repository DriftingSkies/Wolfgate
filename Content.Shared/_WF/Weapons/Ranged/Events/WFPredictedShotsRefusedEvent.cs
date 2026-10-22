using Robust.Shared.Serialization;

namespace Content.Shared._WF.Weapons.Ranged.Events;

/// <summary>
/// Sent to a shooter when the server fired fewer rounds than their client predicted, naming the predicted copies to drop.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFPredictedShotsRefusedEvent(List<int> clientIds) : EntityEventArgs
{
    /// <summary>Client-side ids of the predicted projectiles the server never fired.</summary>
    public readonly List<int> ClientIds = clientIds;
}
