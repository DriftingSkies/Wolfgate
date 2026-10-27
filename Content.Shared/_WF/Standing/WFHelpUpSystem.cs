using Content.Shared.Bed.Sleep;
using Content.Shared.Damage.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Standing;

/// <summary>
/// Hugging or hand-clicking a knocked-down body cuts its stun and knockdown so it gets up sooner.
/// </summary>
public sealed class WFHelpUpSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StatusEffectsSystem _statusEffect = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    /// <summary>Stun and knockdown time removed per help.</summary>
    public static readonly TimeSpan HelpAmount = TimeSpan.FromSeconds(2);

    /// <summary>Minimum time between helps on the same body.</summary>
    public static readonly TimeSpan HelpCooldown = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<KnockedDownComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<KnockedDownComponent, InteractionSuccessEvent>(OnHugSuccess);
        SubscribeLocalEvent<KnockedDownComponent, InteractionFailureEvent>(OnHugFailure);
    }

    // A body with a hug popup is helped through the hug's events instead, so the hug still plays.
    private void OnInteractHand(Entity<KnockedDownComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || HasComp<InteractionPopupComponent>(ent))
            return;

        if (!TryHelpUp(ent, args.User))
            return;

        _audio.PlayPredicted(ent.Comp.StunAttemptSound, ent, args.User);
        args.Handled = true;
    }

    private void OnHugSuccess(Entity<KnockedDownComponent> ent, ref InteractionSuccessEvent args)
    {
        TryHelpUp(ent, args.User);
    }

    private void OnHugFailure(Entity<KnockedDownComponent> ent, ref InteractionFailureEvent args)
    {
        TryHelpUp(ent, args.User);
    }

    /// <summary>
    /// Removes <see cref="HelpAmount"/> from the target's stun and knockdown, unless helped too recently.
    /// </summary>
    public bool TryHelpUp(EntityUid target, EntityUid user)
    {
        if (user == target || HasComp<SleepingComponent>(target))
            return false;

        var helped = EnsureComp<WFHelpedUpComponent>(target);
        var now = _timing.CurTime;
        if (now < helped.NextHelp)
            return false;

        helped.NextHelp = now + HelpCooldown;
        Dirty(target, helped);

        _statusEffect.TryRemoveTime(target, "Stun", HelpAmount);
        _statusEffect.TryRemoveTime(target, "KnockedDown", HelpAmount);

        var targetName = Identity.Entity(target, EntityManager);
        _popup.PopupPredicted(
            Loc.GetString("wf-help-up-self", ("target", targetName)),
            Loc.GetString("wf-help-up-others", ("user", Identity.Entity(user, EntityManager)), ("target", targetName)),
            target,
            user);

        // Stamina crit's re-stun immunity ends with the stun, so a helped-up body can be batoned again on time.
        if (TryComp<StaminaComponent>(target, out var stamina) && stamina.Critical)
        {
            stamina.NextUpdate -= HelpAmount;
            Dirty(target, stamina);
        }

        return true;
    }
}
