using Content.Shared.Access.Systems;
using Content.Shared.Lock;
using Content.Shared.Popups;

namespace Content.Server._WG.LockableButtons;

/// <summary>
/// Gates a locked button or switch: someone its access reader admits presses it and it stays locked, anyone else
/// is denied. Unlocking it, from its verb, opens it to everyone.
/// </summary>
public sealed partial class LockableSwitchSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LockComponent, SwitchPressAttemptEvent>(OnPressAttempt);
    }

    private void OnPressAttempt(Entity<LockComponent> ent, ref SwitchPressAttemptEvent args)
    {
        if (!ent.Comp.Locked)
            return;

        // A lock that unlocks on click takes the click itself, so the switch only stays out of its way.
        if (ent.Comp.UnlockOnClick)
        {
            args.Cancelled = true;
            return;
        }

        if (_accessReader.IsAllowed(args.User, ent))
            return;

        _popup.PopupEntity(Loc.GetString("lock-comp-has-user-access-fail"), ent, args.User);
        args.Cancelled = true;
        args.Handled = true;
    }
}

/// <summary>
/// Raised on a signal switch before a press fires it. Cancelling stops the press; <see cref="Handled"/> also ends
/// the click, so nothing else acts on it.
/// </summary>
[ByRefEvent]
public record struct SwitchPressAttemptEvent(EntityUid User, bool Cancelled = false, bool Handled = false);
