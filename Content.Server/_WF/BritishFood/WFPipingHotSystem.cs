using Content.Server.Nutrition;
using Content.Server.Nutrition.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Shared._Goobstation.EatToGrow;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.BritishFood;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Nutrition;
using Content.Shared.Popups;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.BritishFood;

/// <summary>Cools piping hot food, burns whoever eats it while hot and puts them off the next bite.</summary>
public sealed partial class WFPipingHotSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPipingHotComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFPipingHotComponent, ConsumeDoAfterEvent>(OnBiteAttempt, before: [typeof(FoodSystem)]);
        SubscribeLocalEvent<WFPipingHotComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFPipingHotComponent, FoodSlicedEvent>(OnSliced);
        SubscribeLocalEvent<BodyComponent, AfterEatingEvent>(OnAteBite);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFPipingHotComponent>();
        while (query.MoveNext(out var uid, out var hot))
        {
            if (now >= hot.CoolAt)
                RemCompDeferred<WFPipingHotComponent>(uid);
        }
    }

    private void OnMapInit(Entity<WFPipingHotComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.CoolAt == TimeSpan.Zero)
            ent.Comp.CoolAt = _timing.CurTime + _random.Next(ent.Comp.MinDuration, ent.Comp.MaxDuration);
    }

    /// <summary>Refuses the bite while a burned tongue recovers, which stops eating.</summary>
    private void OnBiteAttempt(Entity<WFPipingHotComponent> ent, ref ConsumeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } eater || _timing.CurTime >= ent.Comp.PausedUntil ||
            !CanScald(ent, eater))
            return;

        args.Handled = true;
        args.Repeat = false;
        _popup.PopupEntity(Loc.GetString("wf-piping-hot-too-hot"), eater, args.User);
    }

    /// <summary>Burns the eater once a bite of hot food has gone down.</summary>
    private void OnAteBite(Entity<BodyComponent> ent, ref AfterEatingEvent args)
    {
        if (!TryComp<WFPipingHotComponent>(args.Food, out var hot) || !CanScald((args.Food, hot), ent))
            return;

        // The burn lands inside the bite's do-after, which must not be cancelled by it.
        hot.PausedUntil = _timing.CurTime + hot.ScaldPause;
        _damage.TryChangeDamage(ent, hot.Burn, interruptsDoAfters: false, origin: args.Food, canSever: false,
            targetPart: TargetBodyPart.Head);
        _popup.PopupEntity(Loc.GetString("wf-piping-hot-burn-self", ("food", args.Food)), ent, ent,
            PopupType.MediumCaution);
        _popup.PopupEntity(Loc.GetString("wf-piping-hot-burn-others", ("eater", Identity.Entity(ent, EntityManager))),
            ent, Filter.PvsExcept(ent), true);
    }

    private void OnExamined(Entity<WFPipingHotComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-piping-hot-examine"));
    }

    /// <summary>Slices of hot food stay as hot as what they were cut from.</summary>
    private void OnSliced(Entity<WFPipingHotComponent> ent, ref FoodSlicedEvent args)
    {
        PassHeat(ent.AsNullable(), args.Slice);
    }

    /// <summary>Whether the heat hurts this eater: not when they are numb to pain or built for heat.</summary>
    private bool CanScald(Entity<WFPipingHotComponent> ent, EntityUid eater)
    {
        if (HasComp<PainNumbnessComponent>(eater))
            return false;

        return TryComp<TemperatureComponent>(eater, out var temperature) &&
               temperature.HeatDamageThreshold < ent.Comp.HeatProofThreshold;
    }

    /// <summary>Gives food made from hot food the heat it has left.</summary>
    public void PassHeat(Entity<WFPipingHotComponent?> from, EntityUid to)
    {
        if (!Resolve(from, ref from.Comp, false) || from.Comp.CoolAt <= _timing.CurTime)
            return;

        EnsureComp<WFPipingHotComponent>(to).CoolAt = from.Comp.CoolAt;
    }
}
