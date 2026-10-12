using Content.Shared._WF.Administration;
using Content.Shared.Database;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.Console;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server._WF.Administration.Systems;

/// <summary>
/// Right-click verbs under the Admin category for the Wolfgate admin tooling.
/// </summary>
public sealed partial class WolfgateAdminVerbSystem : EntitySystem
{
    [Dependency] private IConGroupController _groupController = default!;
    [Dependency] private IServerConsoleHost _console = default!;
    [Dependency] private GhostOfferSystem _ghostOffer = default!;
    [Dependency] private SecondChanceSystem _secondChance = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!TryComp<ActorComponent>(args.User, out var actor))
            return;

        var player = actor.PlayerSession;

        // Spawn as Outfit: the picker opens client-side, the spawn command itself is admin-gated.
        if (_groupController.CanCommand(player, WolfgateAdminCommands.SpawnOutfit))
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wf-admin-verbs-spawn-outfit"),
                Message = Loc.GetString("wf-admin-verbs-spawn-outfit-description"),
                Category = VerbCategory.Admin,
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/outfit.svg.192dpi.png")),
                Act = () => _console.RemoteExecuteCommand(player, $"{WolfgateAdminCommands.SpawnOutfitUi} {GetNetEntity(args.Target)}"),
                Impact = LogImpact.Medium,
            });
        }

        // Offer to Ghosts: a mob nobody is playing, or an open ghost role, with its default texts.
        if (_groupController.CanCommand(player, WolfgateAdminCommands.OfferToGhosts)
            && _ghostOffer.CanOffer(args.Target))
        {
            var target = args.Target;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wf-admin-verbs-offer-to-ghosts"),
                Message = Loc.GetString("wf-admin-verbs-offer-to-ghosts-description"),
                Category = VerbCategory.Admin,
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/sentient.svg.192dpi.png")),
                Act = () =>
                {
                    var offered = _ghostOffer.TryOffer(target, player, null, null, null, out var message);
                    _popup.PopupCursor(message, player, offered ? PopupType.Medium : PopupType.MediumCaution);
                },
                Impact = LogImpact.Medium,
            });
        }

        // Offer Second Chance: asks the body's player; a yes returns them to it and rejuvenates it.
        if (_groupController.CanCommand(player, WolfgateAdminCommands.SecondChance)
            && _secondChance.CanOffer(args.Target))
        {
            var target = args.Target;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wf-admin-verbs-second-chance"),
                Message = Loc.GetString("wf-admin-verbs-second-chance-description"),
                Category = VerbCategory.Admin,
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png")),
                Act = () =>
                {
                    var offered = _secondChance.TryOffer(target, player, out var message);
                    _popup.PopupCursor(message, player, offered ? PopupType.Medium : PopupType.MediumCaution);
                },
                Impact = LogImpact.Medium,
            });
        }
    }
}
