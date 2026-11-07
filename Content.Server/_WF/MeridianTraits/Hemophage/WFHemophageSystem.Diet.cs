using Content.Server.Medical;
using Content.Shared._Goobstation.EatToGrow;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.MeridianTraits.Hemophage;
using Content.Server.Body.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;

namespace Content.Server._WF.MeridianTraits.Hemophage;

public sealed partial class WFHemophageSystem
{
    [Dependency] private VomitSystem _vomit = default!;

    /// <summary>Bites and sips seen this tick that the tumor objects to, judged once they have reached the stomach.</summary>
    private readonly Dictionary<EntityUid, PendingIngestion> _pending = new();

    private sealed class PendingIngestion
    {
        public bool BadFood;
        public bool BadDrink;
    }

    private void InitializeDiet()
    {
        SubscribeLocalEvent<WFHemophageComponent, WFDrankEvent>(OnDrank);
        SubscribeLocalEvent<WFHemophageComponent, AfterEatingEvent>(OnAfterEating);
    }

    private void OnDrank(Entity<WFHemophageComponent> ent, ref WFDrankEvent args)
    {
        if (!IsSafeDrink(ent.Comp, args.Drunk))
            Pending(ent).BadDrink = true;
    }

    private void OnAfterEating(Entity<WFHemophageComponent> ent, ref AfterEatingEvent args)
    {
        // Pills are medicine, not food.
        if (HasComp<PillComponent>(args.Food))
            return;

        var bloody = _tag.HasTag(args.Food, ent.Comp.BloodyFoodTag)
            || MetaData(args.Food).EntityPrototype is { } proto && ent.Comp.BloodyFoods.Contains(proto.ID);
        if (!bloody)
            Pending(ent).BadFood = true;
    }

    private PendingIngestion Pending(EntityUid uid)
    {
        if (!_pending.TryGetValue(uid, out var pending))
        {
            pending = new PendingIngestion();
            _pending[uid] = pending;
        }

        return pending;
    }

    private bool IsSafeDrink(WFHemophageComponent comp, Solution solution)
    {
        if (solution.Volume <= FixedPoint2.Zero)
            return true;

        var safe = FixedPoint2.Zero;
        foreach (var quantity in solution.Contents)
        {
            if (quantity.Reagent.Prototype == comp.RestorationReagent)
                return true;

            if (comp.SafeReagents.Contains(quantity.Reagent.Prototype))
                safe += quantity.Quantity;
        }

        return safe >= solution.Volume * comp.SafeDrinkRatio;
    }

    private void UpdatePendingIngestion()
    {
        if (_pending.Count == 0)
            return;

        foreach (var (uid, pending) in _pending)
        {
            if (!TryComp<WFHemophageComponent>(uid, out var comp) || !TryComp<BodyComponent>(uid, out var body) || Tolerates(uid, comp))
                continue;

            var rejected = pending.BadFood && HasCorrupted<StomachComponent>((uid, body))
                || pending.BadDrink && HasCorrupted<LiverComponent>((uid, body));

            if (!rejected)
                continue;

            _popup.PopupEntity(Loc.GetString("wf-hemophage-tasted-awful"), uid, uid, PopupType.MediumCaution);
            Sicken((uid, comp), comp.VileNausea);
            _vomit.Vomit(uid, 0f, 0f);
        }

        _pending.Clear();
    }

    /// <summary>The tumor ignores what goes down while dormant, under a taste suppressor, or once it has learned to.</summary>
    private bool Tolerates(EntityUid uid, WFHemophageComponent comp)
    {
        if (comp.Dormant)
            return true;

        if (TryComp<WFHemophageTraitComponent>(uid, out var trait) && trait.Masquerade)
            return true;

        return TryComp<WFTasteSuppressedComponent>(uid, out var suppressed) && _timing.CurTime < suppressed.Until;
    }
}
