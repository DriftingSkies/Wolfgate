using Content.Server.Chat.Managers;
using Content.Server.PDA;
using Content.Shared._WF.Cyborgs;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chat;
using Content.Shared.PDA;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.Cyborgs;

/// <summary>
/// Names a cyborg's built-in PDA after the cyborg and shows the cyborg its app notifications.
/// </summary>
public sealed class WFBorgPdaSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private PdaSystem _pda = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFBorgPdaComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFBorgPdaComponent, CartridgeLoaderNotificationSentEvent>(OnNotification);
        SubscribeLocalEvent<EntityRenamedEvent>(OnRenamed);
    }

    private void OnMapInit(Entity<WFBorgPdaComponent> ent, ref MapInitEvent args)
    {
        SetOwner(ent, Name(ent));
    }

    private void OnRenamed(ref EntityRenamedEvent ev)
    {
        if (HasComp<WFBorgPdaComponent>(ev.Uid))
            SetOwner(ev.Uid, ev.NewName);
    }

    private void SetOwner(EntityUid borg, string name)
    {
        if (TryComp<PdaComponent>(borg, out var pda))
            _pda.SetOwner(borg, pda, borg, name);
    }

    // The PDA system only tells whoever carries a PDA; a cyborg is its own.
    private void OnNotification(Entity<WFBorgPdaComponent> ent, ref CartridgeLoaderNotificationSentEvent args)
    {
        if (!TryComp<ActorComponent>(ent, out var actor))
            return;

        var message = FormattedMessage.EscapeText(args.Message);
        var wrapped = Loc.GetString("pda-notification-message", ("header", args.Header), ("message", message));
        _chat.ChatMessageToOne(ChatChannel.Notifications, message, wrapped, EntityUid.Invalid, false, actor.PlayerSession.Channel);
    }
}
