using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Buckle;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Verbs;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared._WG.ExtraInteractions;

/// <summary>
/// Lets a mob hold out its active item so anyone in reach can take it, without dropping it first.
/// </summary>
public sealed partial class WGItemOfferSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>How far in front of the offerer the item's icon floats, in tiles.</summary>
    public const float IconDistance = 0.55f;

    private static readonly SpriteSpecifier PickupIcon =
        new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/pickup.svg.192dpi.png"));

    private static readonly SpriteSpecifier DropIcon =
        new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/drop.svg.192dpi.png"));

    public override void Initialize()
    {
        base.Initialize();

        // Ahead of unbuckling, helping up and the pat-and-hug popups, which would otherwise answer the click first.
        SubscribeLocalEvent<WGItemOfferComponent, InteractHandEvent>(OnInteractHand,
            before: new[] { typeof(InteractionPopupSystem), typeof(SharedBuckleSystem), typeof(SharedStunSystem) });
        SubscribeLocalEvent<WGItemOfferComponent, GetVerbsEvent<InteractionVerb>>(OnGetTakeVerbs);
        SubscribeLocalEvent<WGItemOfferComponent, DidUnequipHandEvent>(OnUnequipped);
        SubscribeLocalEvent<WGItemOfferComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WGItemOfferComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<HandsComponent, GetVerbsEvent<Verb>>(OnGetOfferVerbs);
        SubscribeAllEvent<WGTakeOfferedItemEvent>(OnTakeRequest);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.WGOfferItem, InputCmdHandler.FromDelegate(OnOfferKey, handle: false, outsidePrediction: false))
            .Register<WGItemOfferSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGItemOfferSystem>();
    }

    /// <summary>World position of the offered item's icon, half a tile out the way the offerer faces.</summary>
    public Vector2 GetIconPosition(EntityUid offerer)
    {
        var (position, rotation) = _transform.GetWorldPositionRotation(offerer);
        return position + rotation.GetCardinalDir().ToVec() * IconDistance;
    }

    private void OnOfferKey(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { } user || !Exists(user))
            return;

        if (TryComp<WGItemOfferComponent>(user, out var offer))
            StopOffer((user, offer));
        else
            TryOffer(user);
    }

    /// <summary>Starts offering the user's active item.</summary>
    public bool TryOffer(EntityUid user)
    {
        if (HasComp<WGItemOfferComponent>(user) || !_actionBlocker.CanInteract(user, null))
            return false;

        if (!_hands.TryGetActiveHand(user, out var hand) || hand.HeldEntity is not { } item || HasComp<VirtualItemComponent>(item))
        {
            _popup.PopupClient(Loc.GetString("wg-item-offer-nothing"), user, user);
            return false;
        }

        if (!_hands.CanDropHeld(user, hand, checkActionBlocker: false))
        {
            _popup.PopupClient(Loc.GetString("wg-item-offer-stuck", ("item", item)), user, user);
            return false;
        }

        var offer = EnsureComp<WGItemOfferComponent>(user);
        offer.Item = item;
        Dirty(user, offer);

        _popup.PopupPredicted(Loc.GetString("wg-item-offer-start-self", ("item", item)),
            Loc.GetString("wg-item-offer-start-others", ("user", user), ("item", item)),
            user,
            user);
        return true;
    }

    /// <summary>Withdraws the offer, keeping the item in hand.</summary>
    public void StopOffer(Entity<WGItemOfferComponent> ent)
    {
        if (ent.Comp.Item is { } item)
            _popup.PopupClient(Loc.GetString("wg-item-offer-stop", ("item", item)), ent, ent);

        RemComp(ent, ent.Comp);
    }

    /// <summary>Moves the offered item into the taker's free hand.</summary>
    public bool TryTake(Entity<WGItemOfferComponent> ent, EntityUid taker)
    {
        if (taker == ent.Owner || ent.Comp.Item is not { } item)
            return false;

        if (!_hands.IsHolding(ent.Owner, item) || !_actionBlocker.CanInteract(ent, null))
        {
            RemComp(ent, ent.Comp);
            return false;
        }

        if (!_actionBlocker.CanInteract(taker, ent) || !_interaction.InRangeUnobstructed(taker, ent.Owner))
            return false;

        if (!_hands.CanPickupAnyHand(taker, item))
        {
            _popup.PopupClient(Loc.GetString("wg-item-offer-hands-full"), taker, taker);
            return false;
        }

        // Before the drop, which would otherwise end the offer as if it fell.
        RemComp(ent, ent.Comp);

        _hands.TryDrop(ent, item, checkActionBlocker: false, doDropInteraction: false);
        _hands.PickupOrDrop(taker, item, checkActionBlocker: false);

        _popup.PopupPredicted(Loc.GetString("wg-item-offer-take-self", ("item", item), ("offerer", ent.Owner)),
            Loc.GetString("wg-item-offer-take-others", ("taker", taker), ("item", item), ("offerer", ent.Owner)),
            taker,
            taker);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(taker):user} took {ToPrettyString(item):item} offered by {ToPrettyString(ent.Owner):target}");
        return true;
    }

    private void OnInteractHand(Entity<WGItemOfferComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || args.User == ent.Owner)
            return;

        args.Handled = TryTake(ent, args.User);
    }

    private void OnTakeRequest(WGTakeOfferedItemEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } taker
            || !TryGetEntity(ev.Offerer, out var offerer)
            || !TryComp<WGItemOfferComponent>(offerer, out var offer))
            return;

        TryTake((offerer.Value, offer), taker);
    }

    private void OnGetTakeVerbs(Entity<WGItemOfferComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || args.User == ent.Owner || ent.Comp.Item is not { } item)
            return;

        var user = args.User;
        args.Verbs.Add(new InteractionVerb
        {
            Text = Loc.GetString("wg-item-offer-verb-take", ("item", item)),
            Icon = PickupIcon,
            Act = () => TryTake(ent, user),
        });
    }

    private void OnGetOfferVerbs(Entity<HandsComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (args.User != ent.Owner || !args.CanInteract)
            return;

        var user = args.User;
        if (TryComp<WGItemOfferComponent>(user, out var offer))
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wg-item-offer-verb-stop"),
                Icon = DropIcon,
                Act = () => StopOffer((user, offer)),
            });
            return;
        }

        if (_hands.GetActiveItem((user, ent.Comp)) is not { } item || HasComp<VirtualItemComponent>(item))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("wg-item-offer-verb-offer", ("item", item)),
            Icon = PickupIcon,
            Act = () => TryOffer(user),
        });
    }

    private void OnUnequipped(Entity<WGItemOfferComponent> ent, ref DidUnequipHandEvent args)
    {
        if (args.Unequipped == ent.Comp.Item)
            RemCompDeferred(ent, ent.Comp);
    }

    private void OnMobStateChanged(Entity<WGItemOfferComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            RemCompDeferred(ent, ent.Comp);
    }

    private void OnExamined(Entity<WGItemOfferComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Item is { } item)
            args.PushMarkup(Loc.GetString("wg-item-offer-examine", ("user", ent.Owner), ("item", item)));
    }
}
