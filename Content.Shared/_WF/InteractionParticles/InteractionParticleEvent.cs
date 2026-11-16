using Robust.Shared.Serialization;

namespace Content.Shared._WF.InteractionParticles;

/// <summary>
/// Tells clients to play an interaction particle.
/// </summary>
/// <remarks>
/// Ported from Starlight's <c>StellarInteractionParticleEvent</c>. In-hand particles are only ever sent to the actor.
/// </remarks>
[Serializable, NetSerializable]
public sealed class InteractionParticleEvent(NetEntity performer, NetEntity? used, NetEntity target, bool isClientEvent, InteractionParticleType type) : EntityEventArgs
{
    public NetEntity Performer = performer;

    public NetEntity? Used = used;

    public NetEntity Target = target;

    public TimeSpan Cooldown = TimeSpan.FromSeconds(0.25);

    /// <summary>
    /// Workaround for event subscription not working w/ the session overload.
    /// </summary>
    public bool IsClientEvent = isClientEvent;

    public InteractionParticleType Type = type;
}

[Serializable, NetSerializable]
public enum InteractionParticleType
{
    Use,
    Pull,

    /// <summary>
    /// A particle parented to the actor for interactions happening in their hands, pockets or storage.
    /// </summary>
    InHand,
}
