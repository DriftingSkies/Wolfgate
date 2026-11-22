using Content.Shared.Mind;
using Content.Shared.Store;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipTiers;

/// <summary>
/// The buyer a listing condition is checking. The store hands conditions the buyer's mind, not the body that carries
/// the faction and the player session.
/// </summary>
public static class WGListingBuyer
{
    /// <summary>The body the buyer's mind owns, or the buyer itself when it is not a mind.</summary>
    public static EntityUid Body(ListingConditionArgs args)
    {
        return args.EntityManager.TryGetComponent<MindComponent>(args.Buyer, out var mind) && mind.OwnedEntity is { } body
            ? body
            : args.Buyer;
    }

    /// <summary>The buyer's player, from its mind or the session on its body.</summary>
    public static NetUserId? Player(IEntityManager entities, EntityUid buyer)
    {
        if (entities.TryGetComponent<MindComponent>(buyer, out var mind))
            return mind.UserId;
        return entities.TryGetComponent<ActorComponent>(buyer, out var actor) ? actor.PlayerSession.UserId : null;
    }
}
