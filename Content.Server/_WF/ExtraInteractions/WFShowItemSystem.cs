using Content.Server.Chat.Systems;
using Content.Shared._WF.ExtraInteractions;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Pointing;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.ExtraInteractions;

/// <summary>
/// Checks a show request, then sends the bubble to everyone nearby and an emote line to chat.
/// </summary>
public sealed class WFShowItemSystem : SharedWFShowItemSystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedPointingSystem _pointing = default!;

    private TimeSpan _cooldown;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WFShowItemRequestEvent>(OnRequest);

        // Shares the pointing cooldown, as it stands in for a point.
        Subs.CVar(_config, CCVars.PointingCooldownSeconds, v => _cooldown = TimeSpan.FromSeconds(v), true);
    }

    private void OnRequest(WFShowItemRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user
            || !TryGetEntity(ev.Item, out var item)
            || !IsCarried(user, item.Value)
            || !_pointing.CanPoint(user))
            return;

        var cooldown = EnsureComp<WFShowItemCooldownComponent>(user);
        if (_timing.CurTime < cooldown.NextShow)
            return;

        cooldown.NextShow = _timing.CurTime + _cooldown;

        EntProtoId? prototype = MetaData(item.Value).EntityPrototype is { } proto ? proto.ID : null;
        RaiseNetworkEvent(new WFShowItemEvent(GetNetEntity(user), GetNetEntity(item.Value), prototype),
            Filter.Pvs(user, entityManager: EntityManager));

        _chat.TrySendInGameICMessage(user,
            Loc.GetString("wf-show-item-emote", ("user", user), ("item", item.Value)),
            InGameICChatType.Emote,
            ChatTransmitRange.Normal,
            ignoreActionBlocker: true);
    }
}
