using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Traders;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Robust.Shared.Player;

namespace Content.Server._WG.Traders;

/// <summary>
/// Stamps a spawning player's ID card with who it belongs to, and an unstamped card with the buyer of the ship it holds.
/// </summary>
public sealed partial class IdCardOwnerSystem : EntitySystem
{
    [Dependency] private SharedIdCardSystem _idCard = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
        SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnShipPurchased);
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (!_idCard.TryFindIdCard(ev.Mob, out var idCard))
            return;

        var owner = EnsureComp<IdCardOwnerComponent>(idCard);
        owner.UserId = ev.Player.UserId;
        owner.CharacterName = ev.Profile.Name;
    }

    /// <summary>A card got after spawning (an agent ID) has no owner; whoever buys a ship on it becomes its owner.</summary>
    private void OnShipPurchased(ShipyardShuttlePurchaseEvent ev)
    {
        if (!TryComp<ActorComponent>(ev.Purchaser, out var actor))
            return;

        var query = EntityQueryEnumerator<ShuttleDeedComponent, IdCardComponent>();
        while (query.MoveNext(out var uid, out var deed, out _))
        {
            if (deed.ShuttleUid != ev.Shuttle || HasComp<IdCardOwnerComponent>(uid))
                continue;

            var owner = AddComp<IdCardOwnerComponent>(uid);
            owner.UserId = actor.PlayerSession.UserId;
            owner.CharacterName = Name(ev.Purchaser);
        }
    }

    /// <summary>
    /// Stamps a card with the player controlling <paramref name="mob"/>. A card that already has an owner keeps it
    /// unless <paramref name="overwrite"/> is set.
    /// </summary>
    public void Stamp(EntityUid idCard, EntityUid mob, bool overwrite = false)
    {
        if (!TryComp<ActorComponent>(mob, out var actor) || !overwrite && HasComp<IdCardOwnerComponent>(idCard))
            return;

        var owner = EnsureComp<IdCardOwnerComponent>(idCard);
        owner.UserId = actor.PlayerSession.UserId;
        owner.CharacterName = Name(mob);
    }

    /// <summary>
    /// True when the ID card was issued to the player currently controlling <paramref name="mob"/>.
    /// </summary>
    public bool IsOwnedBy(EntityUid idCard, EntityUid mob)
    {
        if (!TryComp<IdCardOwnerComponent>(idCard, out var owner))
            return false;

        if (!TryComp<ActorComponent>(mob, out var actor))
            return false;

        return actor.PlayerSession.UserId == owner.UserId;
    }
}
