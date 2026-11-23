using Content.Server._WG.Wolfmed.Life;
using Content.Server._WG.Wolfmed.Stasis;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Augments;
using Content.Shared._WG.MeridianTraits.Hemophage;
using Content.Shared.Body.Systems;
using Content.Shared.Emp;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WG.Augments;

/// <summary>
/// Shocks a body with a reviver when its heart has stopped. Every shock is one of Wolfmed's: it is refused for what
/// the paddles are refused for, and restarts the heart on the paddles' odds. While it charges it holds the body's
/// bleeding and runs its reserve of blood in, so the heart it restarts has something to pump and its owner wakes.
/// </summary>
public sealed class WGReviverSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedRevivalSystem _revival = default!;
    [Dependency] private WolfmedStasisSystem _stasis = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGReviverComponent, EmpPulseEvent>(OnEmp);
    }

    private void OnEmp(Entity<WGReviverComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        ent.Comp.NextShock = null;
        var until = _timing.CurTime + ent.Comp.EmpLockout;
        if (ent.Comp.ReadyAt < until)
            ent.Comp.ReadyAt = until;
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Interval;
        var query = EntityQueryEnumerator<WGReviverComponent>();
        while (query.MoveNext(out var uid, out var reviver))
        {
            Tick((uid, reviver));
        }
    }

    /// <summary>
    /// One look at the body: starts charging when its heart has stopped, holds the bleeding and runs blood in while
    /// it charges, and shocks when the charge is up.
    /// </summary>
    public void Tick(Entity<WGReviverComponent> ent)
    {
        var reviver = ent.Comp;
        var now = _timing.CurTime;
        if (!_revival.OwnsRevival(ent) || !_life.InArrest(ent) && !_mobState.IsDead(ent))
        {
            // The arrest is over however it ended: shocks given still need the recharge, and it refills the reserve.
            if (reviver.ShocksGiven > 0 && reviver.ReadyAt < now + reviver.Recharge)
                reviver.ReadyAt = now + reviver.Recharge;

            reviver.NextShock = null;
            reviver.ShocksGiven = 0;
            reviver.Infused = 0f;
            return;
        }

        if (now < reviver.ReadyAt || !CanHelp(ent))
        {
            reviver.NextShock = null;
            return;
        }

        if (reviver.NextShock == null)
        {
            reviver.NextShock = now + (reviver.ShocksGiven == 0 ? reviver.ChargeTime : reviver.RetryDelay);
            if (reviver.ShocksGiven == 0)
            {
                _audio.PlayPvs(reviver.ChargeSound, ent);
                _popup.PopupEntity(Loc.GetString("wg-augment-reviver-charging"), ent, ent, PopupType.Medium);
            }
        }

        // A charge waiting on blood keeps the hold going until it shocks.
        var shockAt = reviver.NextShock.Value > now ? reviver.NextShock.Value : now;
        HoldBleeding(ent, shockAt + reviver.HoldTime);
        Infuse(ent);

        // A charge that is up waits for the blood the paddles ask for.
        if (now < reviver.NextShock || _revival.GetRefusal(ent) != null)
            return;

        reviver.NextShock = null;
        reviver.ShocksGiven++;
        var revived = _revival.TryDefibrillate(ent, out _);
        _audio.PlayPvs(reviver.ZapSound, ent);
        _audio.PlayPvs(revived ? reviver.SuccessSound : reviver.FailureSound, ent);
        _popup.PopupEntity(Loc.GetString("wg-augment-reviver-shock", ("target", Identity.Entity(ent, EntityManager))), ent, PopupType.MediumCaution);

        if (revived)
            _revival.OfferReturn(ent);

        if (revived || reviver.ShocksGiven >= reviver.Shocks)
        {
            reviver.ShocksGiven = 0;
            reviver.Infused = 0f;
            reviver.ReadyAt = now + reviver.Recharge;
        }
    }

    /// <summary>
    /// Whether a shock could do this body any good: a heart left to restart, and nothing the paddles refuse for
    /// except blood the reserve can still put back.
    /// </summary>
    private bool CanHelp(Entity<WGReviverComponent> ent)
    {
        // A destroyed heart stops again on the tick it restarts.
        if (_life.GetHeartHealth(ent) is { } heart && heart <= FixedPoint2.Zero)
            return false;

        var refusal = _revival.GetRefusal(ent);
        return refusal == null || refusal == WolfmedRevivalSystem.NoBlood && CanInfuse(ent);
    }

    /// <summary>
    /// Whether it has blood left that this body can take. A hemophage's blood only comes back by feeding, so there it
    /// replaces what a wound took and never what the tumor drank.
    /// </summary>
    private bool CanInfuse(Entity<WGReviverComponent> ent)
    {
        return ent.Comp.Infused < ent.Comp.Reserve && (!HasComp<WGHemophageComponent>(ent) || HasBleed(ent));
    }

    /// <summary>Whether a wound on the body bleeds, or would with nothing holding or dressing it.</summary>
    private bool HasBleed(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            foreach (var wound in _wounds.GetWounds(part))
            {
                if (wound.Comp.State != WoundState.Open)
                    continue;

                if (TryComp(wound, out WoundBleedingComponent? bleeding) && bleeding.BaseRate > bleeding.NaturalClotting ||
                    TryComp(wound, out WoundInternalBleedingComponent? inside) && inside.Severity > FixedPoint2.Zero)
                    return true;
            }
        }

        return false;
    }

    /// <summary>Holds every bleed on the body until then, as a stasis weave does; the weave's pass lifts it.</summary>
    private void HoldBleeding(EntityUid body, TimeSpan until)
    {
        if (TryComp(body, out WolfmedStasisHoldComponent? hold))
        {
            // An Avali's own stasis has no end to move.
            if (hold.HeldUntil < until)
                hold.HeldUntil = until;

            return;
        }

        if (!HasComp<WoundHostComponent>(body))
            return;

        AddComp<WolfmedStasisHoldComponent>(body).HeldUntil = until;
        _stasis.RefreshBleeding(body);
    }

    /// <summary>A second of the reserve, up to the blood at which the brain stops starving and its owner can wake.</summary>
    private void Infuse(Entity<WGReviverComponent> ent)
    {
        if (!CanInfuse(ent))
            return;

        var reviver = ent.Comp;
        var amount = MathF.Min(reviver.InfuseRate * (float) Interval.TotalSeconds, reviver.Reserve - reviver.Infused);
        amount = MathF.Min(amount, _life.GetTransfusionGuidance(ent).ToBrainSafe);
        if (amount > 0f && _bloodstream.TryModifyBloodLevel(ent, amount))
            reviver.Infused += amount;
    }
}
