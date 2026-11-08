using Content.Server._WF.Husbandry.Components;
using Content.Server.Nutrition.Components;
using Content.Shared._WF.Husbandry;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;

namespace Content.Server._WF.Husbandry;

/// <summary>Taming: a tameable animal fed by hand enough times in a short while becomes livestock.</summary>
public sealed partial class WFHusbandrySystem
{
    /// <summary>How far an animal may wander from the hand feeding it before the feeding stops.</summary>
    private const float FeedReach = 2.5f;

    private void InitializeTaming()
    {
        SubscribeLocalEvent<WFTameableComponent, InteractUsingEvent>(OnTameableInteractUsing);
        SubscribeLocalEvent<WFTameableComponent, WFTameFeedDoAfterEvent>(OnTameFeedDoAfter);
    }

    private void OnTameableInteractUsing(Entity<WFTameableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Tamed || !HasComp<FoodComponent>(args.Used))
            return;

        args.Handled = TryStartFeed(args.User, args.Used, ent);
    }

    /// <summary>Starts feeding an untamed animal by hand.</summary>
    public bool TryStartFeed(EntityUid user, EntityUid food, Entity<WFTameableComponent> animal)
    {
        if (animal.Comp.Tamed || !IsAlive(animal))
            return false;

        var args = new DoAfterArgs(EntityManager, user, animal.Comp.Delay, new WFTameFeedDoAfterEvent(), animal, target: animal, used: food)
        {
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = FeedReach,
        };

        return _doAfter.TryStartDoAfter(args);
    }

    private void OnTameFeedDoAfter(Entity<WFTameableComponent> ent, ref WFTameFeedDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } food || ent.Comp.Tamed || !IsAlive(ent))
            return;

        args.Handled = true;
        QueueDel(food);

        var now = _timing.CurTime;
        ent.Comp.Fed.RemoveAll(fed => now - fed > ent.Comp.Window);
        ent.Comp.Fed.Add(now);
        if (ent.Comp.Fed.Count < ent.Comp.Feeds)
        {
            _popup.PopupEntity(Loc.GetString("wf-tame-fed", ("animal", ent.Owner), ("fed", ent.Comp.Fed.Count), ("feeds", ent.Comp.Feeds)),
                args.User, args.User);
            return;
        }

        Tame(ent);
        _popup.PopupEntity(Loc.GetString("wf-tame-tamed", ("animal", ent.Owner)), args.User, args.User);
    }

    /// <summary>Makes a tameable animal livestock.</summary>
    public void Tame(Entity<WFTameableComponent> animal)
    {
        animal.Comp.Tamed = true;
        animal.Comp.Fed.Clear();
        MakeLivestock(animal);
    }

    private bool IsAlive(EntityUid uid)
    {
        return _mobState.IsAlive(uid);
    }
}
