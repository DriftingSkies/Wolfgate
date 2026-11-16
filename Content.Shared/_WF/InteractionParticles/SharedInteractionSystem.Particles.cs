using Content.Shared._WF.InteractionParticles;
using Content.Shared.Inventory.VirtualItem;
using Robust.Shared.Player;

namespace Content.Shared.Interaction;

public abstract partial class SharedInteractionSystem
{
    /// <summary>
    /// Plays an interaction particle from <paramref name="user"/> to <paramref name="target"/> without raising a contact.
    /// </summary>
    /// <param name="predicted">Whether the user's client ran this too and already drew its own particle.</param>
    public void SpawnInteractionParticle(
        EntityUid user,
        EntityUid target,
        EntityUid? used,
        bool predicted,
        InteractionParticleType type = InteractionParticleType.Use)
    {
        // The interaction may have used up either side, or the item.
        if (user == target || TerminatingOrDeleted(user) || TerminatingOrDeleted(target))
            return;

        if (used != null && TerminatingOrDeleted(used.Value))
            used = null;

        if (HasComp<VirtualItemComponent>(target))
            return;

        // Decided here so the server knows the event is private.
        if (type != InteractionParticleType.Pull && IsPrivateInteraction(user, target))
            type = InteractionParticleType.InHand;

        var ev = new InteractionParticleEvent(GetNetEntity(user), GetNetEntity(used), GetNetEntity(target), !_net.IsServer, type);

        if (!_net.IsServer)
        {
            if (_gameTiming.IsFirstTimePredicted)
                RaiseLocalEvent(ev);

            return;
        }

        if (type == InteractionParticleType.InHand)
        {
            // Only the user sees work in their own hands or bags.
            if (!predicted)
                RaiseNetworkEvent(ev, Filter.Entities(user));

            return;
        }

        var filter = predicted
            ? Filter.PvsExcept(user, entityManager: EntityManager)
            : Filter.Pvs(user, entityManager: EntityManager);

        RaiseNetworkEvent(ev, filter);
    }

    /// <summary>
    /// Whether the target is held, worn or stored by the user, so only the user should see the particle.
    /// </summary>
    public bool IsPrivateInteraction(EntityUid user, EntityUid target)
    {
        return _transform.ContainsEntity(user, target);
    }
}
