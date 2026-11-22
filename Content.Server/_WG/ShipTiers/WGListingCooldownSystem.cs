using Content.Shared._WG.ShipTiers;
using Content.Shared.GameTicking;
using Content.Shared.Store;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipTiers;

/// <summary>Per-player cooldowns on listings such as the free T0 voucher, so a free ship can't be printed endlessly.</summary>
public sealed class WGListingCooldownSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<(NetUserId Player, string Key), TimeSpan> _readyAt = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActorComponent, WGListingCooldownEvent>(OnBought);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _readyAt.Clear());
    }

    /// <summary>Whether the buyer, a mind or a body, is off cooldown for this key; a buyer with no player never is.</summary>
    public bool Ready(EntityUid buyer, string key)
    {
        return WGListingBuyer.Player(EntityManager, buyer) is { } player
               && (!_readyAt.TryGetValue((player, key), out var ready) || _timing.CurTime >= ready);
    }

    private void OnBought(EntityUid uid, ActorComponent actor, WGListingCooldownEvent args)
    {
        _readyAt[(actor.PlayerSession.UserId, args.Key)] = _timing.CurTime + args.Cooldown;
    }
}

/// <summary>Lists an item only to a buyer off its per-player cooldown (see <see cref="WGListingCooldownEvent"/>).</summary>
public sealed partial class WGListingCooldownCondition : ListingCondition
{
    [DataField(required: true)]
    public string Key = string.Empty;

    public override bool Condition(ListingConditionArgs args)
    {
        return args.EntityManager.System<WGListingCooldownSystem>().Ready(args.Buyer, Key);
    }
}
