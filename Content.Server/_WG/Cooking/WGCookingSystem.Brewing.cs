using System.Linq;
using Content.Shared._WG.Cooking;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;

namespace Content.Server._WG.Cooking;

/// <summary>Brewing: tea, coffee and the like steep in a kettle of boiling water on the hob.</summary>
public sealed partial class WGCookingSystem
{
    /// <summary>Units of powder a kettle steeps each second of heat.</summary>
    private const float PowderPerSecond = 1f;

    private List<WGBrewRecipePrototype>? _brewRecipes;

    private List<WGBrewRecipePrototype> BrewRecipes()
    {
        return _brewRecipes ??= _proto.EnumeratePrototypes<WGBrewRecipePrototype>()
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.ID)
            .ToList();
    }

    /// <summary>The drink an item brews in a kettle, if any.</summary>
    public WGBrewRecipePrototype? BrewRecipe(EntityUid item)
    {
        return BrewRecipes().FirstOrDefault(r => Matches(item, r.Inputs, r.Tags));
    }

    /// <summary>Whether a pot takes the item: a kettle what brews, any other pot what stews.</summary>
    private bool Takes(Entity<WGPotComponent> pot, EntityUid item)
    {
        return pot.Comp.Kettle ? BrewRecipe(item) != null : StewRecipe(item) != null;
    }

    /// <summary>Steeps a boiling kettle: items until their time is up, powders a little each second.</summary>
    private void Brew(Entity<WGPotComponent> kettle, Entity<SolutionComponent> soln, float heat)
    {
        var solution = soln.Comp.Solution;
        foreach (var job in kettle.Comp.Jobs.ToList())
        {
            job.Left -= heat;
            var water = solution.GetTotalPrototypeQuantity(Water);
            if (job.Left > 0 || water <= FixedPoint2.Zero)
                continue;

            kettle.Comp.Jobs.Remove(job);
            if (!_proto.TryIndex<WGBrewRecipePrototype>(job.Recipe, out var recipe))
                continue;

            var portion = FixedPoint2.Min(FixedPoint2.New(kettle.Comp.Portion * (1f + 0.25f * job.Quality)), water);
            _solutions.RemoveReagent(soln, Water, portion);
            _solutions.TryAddReagent(soln, recipe.Drink.Id, portion);
            _popup.PopupEntity(Loc.GetString("wg-brew-done"), Shown(kettle), PopupType.Medium);
            _audio.PlayPvs(Bubbles, Shown(kettle));
        }

        foreach (var recipe in BrewRecipes())
        {
            if (recipe.Powder is not { } powder || solution.GetTotalPrototypeQuantity(powder.Id) <= FixedPoint2.Zero)
                continue;

            var water = solution.GetTotalPrototypeQuantity(Water).Float();
            var units = MathF.Min(MathF.Min(solution.GetTotalPrototypeQuantity(powder.Id).Float(), PowderPerSecond * heat),
                water / recipe.PowderYield);
            if (units < 0.01f)
                continue;

            _solutions.RemoveReagent(soln, powder.Id, FixedPoint2.New(units));
            _solutions.RemoveReagent(soln, Water, FixedPoint2.New(units * recipe.PowderYield));
            _solutions.TryAddReagent(soln, recipe.Drink.Id, FixedPoint2.New(units * recipe.PowderYield));
            if (solution.GetTotalPrototypeQuantity(powder.Id) <= FixedPoint2.Zero)
            {
                _popup.PopupEntity(Loc.GetString("wg-brew-done"), Shown(kettle), PopupType.Medium);
                _audio.PlayPvs(Bubbles, Shown(kettle));
            }
        }
    }

    /// <summary>A kettle lifted off the hob won't pour while powder is still steeping in it; spent powder with no water left doesn't hold it.</summary>
    private void OnKettlePourAttempt(Entity<WGPotComponent> ent, ref SolutionTransferAttemptEvent args)
    {
        if (args.From != ent.Owner || !ent.Comp.Kettle || !_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution))
            return;
        if (solution.GetTotalPrototypeQuantity(Water) > FixedPoint2.Zero
            && BrewRecipes().Any(r => r.Powder is { } powder && solution.GetTotalPrototypeQuantity(powder.Id) > FixedPoint2.Zero))
            args.Cancel(Loc.GetString("wg-brew-still-steeping"));
    }

    private void ExamineKettle(Entity<WGPotComponent> ent, ExaminedEvent args)
    {
        if (ent.Comp.Jobs.Count > 0)
            args.PushMarkup(Loc.GetString("wg-brew-steeping", ("count", ent.Comp.Jobs.Count)));

        foreach (var job in ent.Comp.Jobs)
        {
            if (_proto.TryIndex<WGBrewRecipePrototype>(job.Recipe, out var recipe) && _proto.TryIndex(recipe.Drink, out var drink))
                args.PushMarkup(Loc.GetString("wg-brew-becoming", ("drink", drink.LocalizedName)));
        }

        if (ent.Comp.Boil >= ent.Comp.BoilAt || !_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution))
            return;

        if (BrewRecipes().Any(r => r.Powder is { } powder && solution.GetTotalPrototypeQuantity(powder.Id) > FixedPoint2.Zero))
            args.PushMarkup(Loc.GetString("wg-brew-powder-waiting"));
    }
}
