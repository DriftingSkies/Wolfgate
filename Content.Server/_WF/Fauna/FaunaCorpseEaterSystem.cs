using Content.Server.Nutrition.Components;
using Content.Server.Popups;
using Content.Shared._WF.Fauna;
using Content.Shared._WF.RMCCore.Damage;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Runs the eat-a-corpse do-after of <see cref="FaunaCorpseEaterComponent"/> mobs.
/// </summary>
public sealed partial class FaunaCorpseEaterSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedRMCDamageableSystem _rmcDamageable = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaCorpseEaterComponent, FaunaEatCorpseDoAfterEvent>(OnEaten);
    }

    /// <summary>
    /// Whether the target is a free-lying corpse, or food if the mob eats food, that the mob can eat.
    /// </summary>
    public bool CanEat(Entity<FaunaCorpseEaterComponent> ent, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || _container.IsEntityInContainer(target))
            return false;

        return _mobState.IsDead(target)
            || ent.Comp.EatCritical && _mobState.IsCritical(target)
            || ent.Comp.Food && HasComp<FoodComponent>(target) && !_mobState.IsAlive(target)
            || ent.Comp.Whitelist != null && !HasComp<MobStateComponent>(target)
                && _whitelist.IsWhitelistPass(ent.Comp.Whitelist, target);
    }

    /// <summary>
    /// Starts eating the target. False if it can't be eaten or the do-after didn't start.
    /// </summary>
    public bool TryStartEating(Entity<FaunaCorpseEaterComponent?> ent, EntityUid target)
    {
        if (!Resolve(ent, ref ent.Comp) || ent.Comp.Eating != null || !CanEat((ent, ent.Comp), target))
            return false;

        var doAfter = new DoAfterArgs(EntityManager, ent, ent.Comp.Delay, new FaunaEatCorpseDoAfterEvent(), ent,
            target: target)
        {
            BreakOnMove = true,
            BreakOnDamage = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return false;

        ent.Comp.Eating = target;
        _popup.PopupEntity(Loc.GetString(ent.Comp.StartPopup, ("user", ent.Owner), ("target", target)), ent);
        return true;
    }

    private void OnEaten(Entity<FaunaCorpseEaterComponent> ent, ref FaunaEatCorpseDoAfterEvent args)
    {
        ent.Comp.Eating = null;
        if (args.Cancelled || args.Handled || args.Target is not { } target || !CanEat(ent, target))
            return;

        args.Handled = true;
        _audio.PlayPvs(ent.Comp.Sound, ent);
        _popup.PopupEntity(Loc.GetString(ent.Comp.DonePopup, ("user", ent.Owner), ("target", target)), ent);

        var heal = ent.Comp.Heal;
        if (ent.Comp.HealTargetFraction > 0 && _thresholds.TryGetDeadThreshold(target, out var dead))
            heal += dead.Value * ent.Comp.HealTargetFraction;

        if (heal > 0)
            _damageable.TryChangeDamage(ent, _rmcDamageable.DistributeHealing(ent.Owner, ent.Comp.HealGroup, heal), true, false);

        if (ent.Comp.Hunger != 0)
            _hunger.ModifyHunger(ent, ent.Comp.Hunger);

        var food = !_mobState.IsDead(target) && !_mobState.IsCritical(target);
        var ev = new FaunaAteEvent(target, food);
        RaiseLocalEvent(ent, ref ev);

        if (food)
        {
            if (ent.Comp.Cocoon is { } itemCocoon)
                Wrap(ent, target, itemCocoon);
            else
                QueueDel(target);
            return;
        }

        if (ent.Comp.Bite != null)
            _damageable.TryChangeDamage(target, ent.Comp.Bite, true, origin: ent);

        // A queued delete still reads as alive, so it is tracked here to skip the cocoon.
        var consumed = false;
        if (ent.Comp.Consume && !TerminatingOrDeleted(target))
        {
            consumed = true;
            if (TryComp<MindContainerComponent>(target, out var mind) && mind.HasMind)
                _body.GibBody(target);
            else
                QueueDel(target);
        }

        if (ent.Comp.Cocoon is { } cocoonProto && !consumed && !TerminatingOrDeleted(target))
            Wrap(ent, target, cocoonProto);

        if (ent.Comp.Spawn is { } spawn)
            Spawn(spawn, Transform(ent).Coordinates);
    }

    private void Wrap(Entity<FaunaCorpseEaterComponent> ent, EntityUid target, EntProtoId cocoonProto)
    {
        var cocoon = Spawn(cocoonProto, Transform(target).Coordinates);
        var container = _container.EnsureContainer<Container>(cocoon, ent.Comp.CocoonContainer);
        _container.Insert(target, container);
    }
}
