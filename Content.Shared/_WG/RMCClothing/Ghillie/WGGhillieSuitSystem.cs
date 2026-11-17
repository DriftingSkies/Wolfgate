using Content.Shared._WG.RMCCore.Stealth;
using Content.Shared.Actions;
using Content.Shared.Buckle.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Shared._WG.RMCClothing.Ghillie;

/// <summary>
/// The ghillie suit's prepare position action, after RMC-14: a do-after, then RMC stealth on the wearer until
/// the cover breaks.
/// </summary>
public sealed partial class WGGhillieSuitSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WGGhillieSuitComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<WGGhillieSuitComponent, WGGhilliePreparePositionEvent>(OnPreparePosition);
        SubscribeLocalEvent<WGGhillieSuitComponent, WGGhillieSuitDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<WGGhillieSuitComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<WGGhillieSuitComponent, InventoryRelayedEvent<SelfBeforeGunShotEvent>>(OnShoot);

        SubscribeLocalEvent<WGGhillieHiddenComponent, MoveInputEvent>(OnMoveInput);
        SubscribeLocalEvent<WGGhillieHiddenComponent, BuckledEvent>(OnBuckled);
        SubscribeLocalEvent<WGGhillieHiddenComponent, PullStartedMessage>(OnPulled);
        SubscribeLocalEvent<WGGhillieHiddenComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnGetActions(Entity<WGGhillieSuitComponent> ent, ref GetItemActionsEvent args)
    {
        if (!TryComp<ClothingComponent>(ent, out var clothing) || args.SlotFlags is not { } slot ||
            (clothing.Slots & slot) == 0)
            return;

        args.AddAction(ref ent.Comp.ActionEntity, ent.Comp.Action);
    }

    private void OnPreparePosition(Entity<WGGhillieSuitComponent> ent, ref WGGhilliePreparePositionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.Performer;

        if (ent.Comp.Enabled)
        {
            SetHidden(ent, user, false);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, ent.Comp.UseDelay, new WGGhillieSuitDoAfterEvent(), ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            CancelDuplicate = true,
            DuplicateCondition = DuplicateConditions.SameTool,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
        {
            _popup.PopupPredicted(Loc.GetString("wg-ghillie-activate-self"),
                Loc.GetString("wg-ghillie-activate-others", ("user", user)), user, user, PopupType.Medium);
        }
    }

    private void OnDoAfter(Entity<WGGhillieSuitComponent> ent, ref WGGhillieSuitDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        // The suit may have come off during the do-after without the wearer moving.
        if (IsWorn(ent, args.User))
            SetHidden(ent, args.User, true);
    }

    /// <summary>Whether the item is worn by the user in one of its clothing slots.</summary>
    private bool IsWorn(EntityUid item, EntityUid user)
    {
        return TryComp<ClothingComponent>(item, out var clothing) &&
               _inventory.TryGetContainingSlot(item, out var slot) &&
               Transform(item).ParentUid == user &&
               (clothing.Slots & slot.SlotFlags) != 0;
    }

    private void OnUnequipped(Entity<WGGhillieSuitComponent> ent, ref GotUnequippedEvent args)
    {
        SetHidden(ent, args.Equipee, false);
    }

    private void OnShoot(Entity<WGGhillieSuitComponent> ent, ref InventoryRelayedEvent<SelfBeforeGunShotEvent> args)
    {
        var user = args.Args.Shooter;
        if (!ent.Comp.Enabled ||
            !TryComp<EntityActiveInvisibleComponent>(user, out var invisible) ||
            !TryComp<RMCPassiveStealthComponent>(user, out var passive))
            return;

        // Each shot adds a step of visibility, which then fades back out over the break delay.
        invisible.Opacity = Math.Min(1f, invisible.Opacity + ent.Comp.AddedOpacityOnShoot);
        Dirty(user, invisible);

        passive.MaxOpacity = invisible.Opacity;
        passive.Delay = ent.Comp.InvisibilityBreakDelay;
        passive.ToggleTime = _timing.CurTime;
        Dirty(user, passive);
    }

    private void OnMoveInput(Entity<WGGhillieHiddenComponent> ent, ref MoveInputEvent args)
    {
        if (args.HasDirectionalMovement)
            Reveal(ent);
    }

    private void OnBuckled(Entity<WGGhillieHiddenComponent> ent, ref BuckledEvent args)
    {
        Reveal(ent);
    }

    private void OnPulled(Entity<WGGhillieHiddenComponent> ent, ref PullStartedMessage args)
    {
        if (args.PulledUid == ent.Owner)
            Reveal(ent);
    }

    private void OnMobStateChanged(Entity<WGGhillieHiddenComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Reveal(ent);
    }

    private void Reveal(Entity<WGGhillieHiddenComponent> ent)
    {
        if (TryComp<WGGhillieSuitComponent>(ent.Comp.Suit, out var suit))
            SetHidden((ent.Comp.Suit.Value, suit), ent, false);
    }

    /// <summary>Hides or reveals the wearer. The server owns the stealth components; clients get them by state.</summary>
    public void SetHidden(Entity<WGGhillieSuitComponent> ent, EntityUid user, bool hidden)
    {
        if (ent.Comp.Enabled == hidden || _net.IsClient)
            return;

        if (hidden)
        {
            var current = CompOrNull<WGGhillieHiddenComponent>(user);

            // Another stealth source owns the stealth components; leave it alone.
            if (current == null &&
                (HasComp<RMCPassiveStealthComponent>(user) || HasComp<EntityActiveInvisibleComponent>(user)))
            {
                _popup.PopupEntity(Loc.GetString("wg-ghillie-already-hidden"), user, user);
                return;
            }

            // A suit and a cloak share one hidden state; the newer item takes it over.
            if (current?.Suit is { } other && other != ent.Owner &&
                TryComp<WGGhillieSuitComponent>(other, out var otherSuit))
            {
                otherSuit.Enabled = false;
                Dirty(other, otherSuit);
            }

            ent.Comp.Enabled = true;
            Dirty(ent);

            var addedTurnInvisible = current?.AddedTurnInvisible ?? !HasComp<EntityTurnInvisibleComponent>(user);
            var turnInvisible = EnsureComp<EntityTurnInvisibleComponent>(user);
            turnInvisible.Enabled = true;
            turnInvisible.UncloakTime = _timing.CurTime;
            Dirty(user, turnInvisible);

            // Fade from fully visible to the suit's opacity over the invisibility delay.
            var passive = EnsureComp<RMCPassiveStealthComponent>(user);
            passive.MaxOpacity = 1f;
            passive.MinOpacity = ent.Comp.Opacity;
            passive.Delay = ent.Comp.InvisibilityDelay;
            passive.Enabled = true;
            passive.ToggleTime = _timing.CurTime;
            Dirty(user, passive);

            var invisible = EnsureComp<EntityActiveInvisibleComponent>(user);
            invisible.Opacity = 1f;
            invisible.DisableMobCollision = true;
            Dirty(user, invisible);

            var marker = EnsureComp<WGGhillieHiddenComponent>(user);
            marker.Suit = ent;
            marker.AddedTurnInvisible = addedTurnInvisible;
            Dirty(user, marker);
            return;
        }

        ent.Comp.Enabled = false;
        Dirty(ent);

        // Only the hidden state this suit set up is torn down.
        if (!TryComp<WGGhillieHiddenComponent>(user, out var hiddenMarker) || hiddenMarker.Suit != ent.Owner)
            return;

        var removeTurnInvisible = hiddenMarker.AddedTurnInvisible;
        RemComp<WGGhillieHiddenComponent>(user);
        RemComp<RMCPassiveStealthComponent>(user);
        RemComp<EntityActiveInvisibleComponent>(user);
        if (removeTurnInvisible)
            RemComp<EntityTurnInvisibleComponent>(user);

        _popup.PopupEntity(Loc.GetString("wg-ghillie-fail-self"), user, user, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("wg-ghillie-fail-others", ("user", user)), user,
            Filter.PvsExcept(user), true, PopupType.Medium);
    }
}
