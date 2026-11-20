using System.Linq;
using Content.Server.Stack;
using Content.Shared._WG.Cooking;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Server.Audio;
using Robust.Shared.Audio;

namespace Content.Server._WG.Cooking;

/// <summary>Seasoning a dish: a pinch of salt, a crack of pepper, a clove of garlic, up to two to a dish.</summary>
public sealed partial class WGMealSystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private AudioSystem _audio = default!;

    /// <summary>Seasonings a dish takes at most.</summary>
    public const int MaxSeasonings = 2;

    private static readonly SoundSpecifier Sprinkle = new SoundPathSpecifier("/Audio/Items/soda_shake.ogg");

    private void InitializeSeasoning()
    {
        SubscribeLocalEvent<WGMealComponent, InteractUsingEvent>(OnSeasonUsing);
    }

    private void OnSeasonUsing(Entity<WGMealComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || SeasoningFor(args.Used) is not { } seasoning)
            return;

        args.Handled = true;
        Season(ent, seasoning, args.Used, args.User);
    }

    // Seasonings by id, sorted once; cleared when prototypes reload.
    private List<WGSeasoningPrototype>? _seasonings;

    /// <summary>The seasoning an item gives, if any: by what it is, or by a reagent it holds a unit of.</summary>
    public WGSeasoningPrototype? SeasoningFor(EntityUid item)
    {
        var id = MetaData(item).EntityPrototype?.ID;
        _seasonings ??= _proto.EnumeratePrototypes<WGSeasoningPrototype>().OrderBy(s => s.ID).ToList();
        foreach (var seasoning in _seasonings)
        {
            if (id != null && seasoning.Items.Any(i => i.Id == id))
                return seasoning;
            if (seasoning.Reagent is { } reagent && HeldReagent(item, reagent) >= FixedPoint2.New(1))
                return seasoning;
        }

        return null;
    }

    private FixedPoint2 HeldReagent(EntityUid item, string reagent)
    {
        var total = FixedPoint2.Zero;
        if (!TryComp<SolutionContainerManagerComponent>(item, out var manager))
            return total;
        foreach (var (_, soln) in _solutions.EnumerateSolutions((item, manager)))
            total += soln.Comp.Solution.GetTotalPrototypeQuantity(reagent);
        return total;
    }

    /// <summary>Seasons a dish: adds its trait if there's room, otherwise doubles its strength bonus.</summary>
    public bool Season(Entity<WGMealComponent> dish, WGSeasoningPrototype seasoning, EntityUid used, EntityUid user)
    {
        if (dish.Comp.Seasonings.Contains(seasoning.ID))
        {
            _popup.PopupEntity(Loc.GetString("wg-season-already", ("seasoning", Loc.GetString(seasoning.Name))), dish, user);
            return false;
        }

        if (dish.Comp.Seasonings.Count >= MaxSeasonings)
        {
            _popup.PopupEntity(Loc.GetString("wg-season-full"), dish, user);
            return false;
        }

        if (!UseUp(used, seasoning))
            return false;

        dish.Comp.Seasonings.Add(seasoning.ID);
        if (seasoning.Trait is { } trait && !dish.Comp.Traits.Contains(trait) && dish.Comp.Traits.Count < 3)
        {
            dish.Comp.Traits.Add(trait);
            dish.Comp.Strength += seasoning.Bonus;
        }
        else
        {
            dish.Comp.Strength += seasoning.Bonus * 2f;
        }

        _audio.PlayPvs(Sprinkle, dish, AudioParams.Default.WithVolume(-4f));
        _popup.PopupEntity(Loc.GetString("wg-season-done", ("dish", dish.Owner), ("seasoning", Loc.GetString(seasoning.Name))), dish, user);
        return true;
    }

    /// <summary>Takes the seasoning from the item: a unit of its reagent, or one of the item.</summary>
    private bool UseUp(EntityUid used, WGSeasoningPrototype seasoning)
    {
        var id = MetaData(used).EntityPrototype?.ID;
        if (id != null && seasoning.Items.Any(i => i.Id == id))
        {
            if (TryComp<StackComponent>(used, out var stack) && stack.Count > 1)
                _stacks.SetCount(used, stack.Count - 1, stack);
            else
                QueueDel(used);
            return true;
        }

        if (seasoning.Reagent is not { } reagent || !TryComp<SolutionContainerManagerComponent>(used, out var manager))
            return false;

        foreach (var (_, soln) in _solutions.EnumerateSolutions((used, manager)))
        {
            if (soln.Comp.Solution.GetTotalPrototypeQuantity(reagent) < FixedPoint2.New(1))
                continue;
            _solutions.RemoveReagent(soln, reagent, FixedPoint2.New(1));
            return true;
        }

        return false;
    }

    private string SeasoningNames(WGMealComponent meal)
    {
        return string.Join(", ", meal.Seasonings.Select(id =>
            _proto.TryIndex<WGSeasoningPrototype>(id, out var seasoning) ? Loc.GetString(seasoning.Name) : id));
    }
}
