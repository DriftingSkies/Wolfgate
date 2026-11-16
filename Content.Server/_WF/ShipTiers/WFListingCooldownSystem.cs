using Content.Shared._WF.ShipTiers;
using Content.Shared.GameTicking;
using Content.Shared.Store;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.ShipTiers;

/// <summary>Per-player cooldowns on listings such as the free T0 voucher, so a free ship can't be printed endlessly.</summary>
public sealed class WFListingCooldownSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<(NetUserId Player, string Key), TimeSpan> _readyAt = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ActorComponent, WFListingCooldownEvent>(OnBought);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _readyAt.Clear());
    }

    /// <summary>Whether the buyer is off cooldown for this key.</summary>
    public bool Ready(EntityUid buyer, string key)
    {
        return !TryComp<ActorComponent>(buyer, out var actor)
               || !_readyAt.TryGetValue((actor.PlayerSession.UserId, key), out var ready)
               || _timing.CurTime >= ready;
    }

    private void OnBought(EntityUid uid, ActorComponent actor, WFListingCooldownEvent args)
    {
        _readyAt[(actor.PlayerSession.UserId, args.Key)] = _timing.CurTime + args.Cooldown;
    }
}

/// <summary>Lists an item only to a buyer off its per-player cooldown (see <see cref="WFListingCooldownEvent"/>).</summary>
public sealed partial class WFListingCooldownCondition : ListingCondition
{
    [DataField(required: true)]
    public string Key = string.Empty;

    public override bool Condition(ListingConditionArgs args)
    {
        return args.EntityManager.System<WFListingCooldownSystem>().Ready(args.Buyer, Key);
    }
}
