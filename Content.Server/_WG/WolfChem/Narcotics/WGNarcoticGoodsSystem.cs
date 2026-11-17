using Content.Server.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Item;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.WolfChem.Narcotics;

/// <summary>
/// The contraband is the drug, not what it comes in. Any item or movable structure with a street drug in one of its
/// solutions, a beaker, syringe, pill, baggie, vial or storage tank, is marked the way the ready-made products are,
/// with the <c>WGNarcotic</c> tag and the contraband, sale tax and sold alert of <c>WGNarcoticMarks</c>, and loses
/// them once the drug is out. A blank stick that takes a copy is marked for good.
/// </summary>
public sealed class WGNarcoticGoodsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private TagSystem _tag = default!;

    public static readonly EntProtoId Marks = "WGNarcoticMarks";
    public static readonly ProtoId<TagPrototype> Tag = "WGNarcotic";

    /// <summary>The reagent group whose priced members are street drugs.</summary>
    public const string Group = "Narcotics";

    private ComponentRegistry? _marks;

    public override void Initialize()
    {
        base.Initialize();
        // Items and what can be unbolted and hauled to a pallet; a thing that is both is simply checked twice.
        SubscribeLocalEvent<ItemComponent, SolutionContainerChangedEvent>((Entity<ItemComponent> ent, ref SolutionContainerChangedEvent args) => OnChanged(ent, args.Solution));
        SubscribeLocalEvent<AnchorableComponent, SolutionContainerChangedEvent>((Entity<AnchorableComponent> ent, ref SolutionContainerChangedEvent args) => OnChanged(ent, args.Solution));
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _marks = null);
    }

    private void OnChanged(EntityUid ent, Solution changed)
    {
        // A creature small enough to carry is not a package.
        if (HasComp<BloodstreamComponent>(ent))
            return;

        if (HoldsNarcotics(changed))
        {
            SetNarcotic(ent, true);
            return;
        }

        // Only what was marked here is unmarked, and only once none of its solutions holds any.
        if (!HasComp<WGNarcoticMarkedComponent>(ent))
            return;

        foreach (var (_, solution) in _solutions.EnumerateSolutions(ent))
        {
            if (HoldsNarcotics(solution.Comp.Solution))
                return;
        }

        SetNarcotic(ent, false);
    }

    /// <summary>Whether a solution holds any street drug: a reagent of the narcotics group that has a price.</summary>
    public bool HoldsNarcotics(Solution solution)
    {
        foreach (var reagent in solution.Contents)
        {
            if (reagent.Quantity > FixedPoint2.Zero
                && _prototype.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var proto)
                && proto.Group == Group
                && proto.PricePerUnit > 0f)
                return true;
        }

        return false;
    }

    /// <summary>Marks a thing as a narcotic, or takes back a mark this system gave; ready-made products keep theirs.</summary>
    public void SetNarcotic(EntityUid uid, bool narcotic)
    {
        if (TerminatingOrDeleted(uid) || GetMarks() is not { } marks)
            return;

        if (narcotic)
        {
            if (_tag.HasTag(uid, Tag))
                return;

            // Whatever the item already had of these stays its own, now and when the mark comes off.
            var marked = EnsureComp<WGNarcoticMarkedComponent>(uid);
            var added = new ComponentRegistry();
            foreach (var (name, entry) in marks)
            {
                if (HasComp(uid, entry.Component.GetType()))
                    continue;

                added.Add(name, entry);
                marked.Added.Add(name);
            }

            EntityManager.AddComponents(uid, added, removeExisting: false);
            _tag.AddTag(uid, Tag);
        }
        else if (TryComp<WGNarcoticMarkedComponent>(uid, out var marked))
        {
            var added = new ComponentRegistry();
            foreach (var name in marked.Added)
            {
                if (marks.TryGetValue(name, out var entry))
                    added.Add(name, entry);
            }

            EntityManager.RemoveComponents(uid, added);
            _tag.RemoveTag(uid, Tag);
            RemComp<WGNarcoticMarkedComponent>(uid);
        }
    }

    /// <summary>The marks prototype's own components, without the transform and metadata every prototype carries.</summary>
    private ComponentRegistry? GetMarks()
    {
        if (_marks != null)
            return _marks;

        if (!_prototype.TryIndex<EntityPrototype>(Marks, out var proto))
            return null;

        _marks = new ComponentRegistry();
        foreach (var (name, entry) in proto.Components)
        {
            if (name != "Transform" && name != "MetaData")
                _marks.Add(name, entry);
        }

        return _marks;
    }
}
