using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Augments;
using Content.Shared._WG.Wolfmed.Consciousness;
using Content.Shared._WG.Wolfmed.Life;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.Augments;

/// <summary>
/// Stands a body with an adrenal booster back up soon after a stamina stun, and stretches a pain shock's adrenaline.
/// </summary>
public sealed partial class WGAdrenalBoosterSystem : EntitySystem
{
    [Dependency] private WolfmedBodyPainSystem _bodyPain = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGAdrenalBoosterComponent, KnockedDownEvent>(OnKnockedDown);
        SubscribeLocalEvent<WolfmedPainShockEvent>(OnPainShock);
    }

    private void OnKnockedDown(Entity<WGAdrenalBoosterComponent> ent, ref KnockedDownEvent args)
    {
        // Only a stamina stun: a slip or a pain shock leaves the booster be.
        if (ent.Comp.SurgeAt != null
            || _timing.CurTime < ent.Comp.ReadyAt
            || !TryComp<StaminaComponent>(ent, out var stamina)
            || !stamina.Critical && stamina.StaminaDamage < stamina.CritThreshold)
            return;

        // The stun is still being applied; the surge cuts it short on a later tick.
        ent.Comp.SurgeAt = _timing.CurTime + ent.Comp.DownTime;
    }

    private void OnPainShock(ref WolfmedPainShockEvent args)
    {
        if (!TryComp<WGAdrenalBoosterComponent>(args.Body, out var booster)
            || !TryComp<PainShockTargetComponent>(args.Body, out var shock)
            || shock.AdrenalineEnds == null)
            return;

        shock.AdrenalineEnds = _timing.CurTime + _bodyPain.AdrenalineTime * booster.PainShockAdrenaline;
        Dirty(args.Body, shock);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGAdrenalBoosterComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.SurgeAt is { } surge && now >= surge)
            {
                comp.SurgeAt = null;
                Surge((uid, comp), now);
            }
            else if (comp.Spent && now >= comp.ReadyAt)
            {
                comp.Spent = false;
                _popup.PopupEntity(Loc.GetString("wg-adrenal-booster-ready"), uid, uid);
            }
        }
    }

    /// <summary>Ends the stun and restores the body's stamina, then starts the cooldown.</summary>
    private void Surge(Entity<WGAdrenalBoosterComponent> ent, TimeSpan now)
    {
        if (!_mobState.IsAlive(ent))
            return;

        // Healing below the threshold, not immediate, is what lets a hard stamina crit end early.
        if (TryComp<StaminaComponent>(ent, out var stamina) && stamina.StaminaDamage > 0f)
            _stamina.TakeStaminaDamage(ent, -stamina.StaminaDamage, stamina, visual: false, immediate: false);

        _status.TryRemoveStatusEffect(ent, "Stun");
        _status.TryRemoveStatusEffect(ent, "KnockedDown");

        ent.Comp.ReadyAt = now + ent.Comp.Cooldown;
        ent.Comp.Spent = true;
        _popup.PopupEntity(Loc.GetString("wg-adrenal-booster-surge"), ent, ent, PopupType.Medium);
        _popup.PopupEntity(Loc.GetString("wg-adrenal-booster-surge-others", ("target", ent.Owner)), ent,
            Filter.PvsExcept(ent), true);
    }
}
