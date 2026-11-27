using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Client.Guidebook.Controls;
using Content.Client.Guidebook.Richtext;
using Content.Client.Message;
using Content.Client.UserInterface.ControlExtensions;
using Content.Shared._WG.Cooking;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Kitchen;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.Cooking;

/// <summary>
/// Guidebook list of recipes as the hearth cooks them, read from the recipe prototypes.
/// <c>Kind</c> is Board, Pan, Oven, Chop, Stew, Brew or Kitchen; Kitchen takes <c>Groups</c>, a comma-separated list
/// of kitchen recipe groups.
/// </summary>
[UsedImplicitly]
public sealed partial class WGRecipeEmbed : BoxContainer, IDocumentTag
{
    /// <summary>Kitchen recipe types the prep board makes.</summary>
    private const int BoardTypes = (int) (MicrowaveRecipeType.Assembler | MicrowaveRecipeType.Oven | MicrowaveRecipeType.Microwave);

    private const int HeatTypes = (int) (MicrowaveRecipeType.Oven | MicrowaveRecipeType.Microwave);

    [Dependency] private IPrototypeManager _proto = default!;

    public WGRecipeEmbed()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
    }

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!args.TryGetValue("Kind", out var kind))
        {
            Logger.GetSawmill("wg.recipes").Error("WGRecipeEmbed is missing its Kind");
            return false;
        }

        var cards = kind switch
        {
            "Board" => BoardCards(),
            "Pan" => CookCards(WGCookMethod.Fry),
            "Oven" => CookCards(WGCookMethod.Bake),
            "Chop" => ChopCards(),
            "Stew" => StewCards(),
            "Brew" => BrewCards(),
            "Kitchen" => KitchenCards(args.GetValueOrDefault("Groups", "Other").Split(',').Select(g => g.Trim()).ToHashSet()),
            _ => null,
        };

        if (cards == null)
        {
            Logger.GetSawmill("wg.recipes").Error($"WGRecipeEmbed has an unknown Kind \"{kind}\"");
            return false;
        }

        foreach (var card in cards.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            AddChild(card);
        }

        control = this;
        return true;
    }

    private IEnumerable<WGRecipeCard> BoardCards()
    {
        foreach (var recipe in _proto.EnumeratePrototypes<WGPrepRecipePrototype>())
        {
            var lines = new List<string> { Loc.GetString("wg-recipe-board", ("items", string.Join(", ", recipe.Ingredients.Select(Counted)))) };
            var shown = recipe.Result.Id;
            if (CookOf(recipe.Result) is { } cook)
            {
                lines.Add(Loc.GetString(cook.Method == WGCookMethod.Bake ? "wg-recipe-then-bake" : "wg-recipe-then-fry",
                    ("result", EntityName(cook.Result))));
                shown = cook.Result.Id;
            }

            yield return Card(shown, lines);
        }
    }

    private IEnumerable<WGRecipeCard> CookCards(WGCookMethod method)
    {
        // A board dish's cooking step is shown on its board card.
        var boardDishes = _proto.EnumeratePrototypes<WGPrepRecipePrototype>().Select(r => r.Result.Id).ToHashSet();
        foreach (var recipe in _proto.EnumeratePrototypes<WGCookRecipePrototype>())
        {
            if (recipe.Method != method || recipe.Inputs.Count > 0 && recipe.Inputs.All(i => boardDishes.Contains(i.Id)))
                continue;

            var items = recipe.Inputs.Select(i => EntityName(i)).Concat(recipe.Tags.Select(t => TagName(t.Id)));
            yield return Card(recipe.Result.Id, new List<string>
            {
                Loc.GetString(method == WGCookMethod.Bake ? "wg-recipe-bake" : "wg-recipe-fry", ("items", string.Join(", ", items))),
            });
        }
    }

    private IEnumerable<WGRecipeCard> ChopCards()
    {
        foreach (var recipe in _proto.EnumeratePrototypes<WGChopRecipePrototype>())
        {
            var items = recipe.Inputs.Select(i => EntityName(i)).Concat(recipe.Tags.Select(t => TagName(t.Id)));
            var lines = new List<string>
            {
                Loc.GetString("wg-recipe-chop", ("items", string.Join(", ", items)), ("count", recipe.Count)),
            };
            if (recipe.Bonus is { } bonus)
                lines.Add(Loc.GetString("wg-recipe-chop-bonus", ("bonus", EntityName(bonus))));
            yield return Card(recipe.Result.Id, lines);
        }
    }

    private IEnumerable<WGRecipeCard> StewCards()
    {
        foreach (var recipe in _proto.EnumeratePrototypes<WGStewRecipePrototype>())
        {
            if (!_proto.TryIndex<ReagentPrototype>(recipe.Soup, out var soup))
                continue;

            var line = recipe.Combine.Count > 0
                ? Loc.GetString("wg-recipe-stew-combine", ("items", string.Join(", ", recipe.Combine.Select(Counted))))
                : Loc.GetString("wg-recipe-stew",
                    ("items", string.Join(", ", recipe.Inputs.Select(i => EntityName(i)).Concat(recipe.Tags.Select(t => TagName(t.Id))))));
            yield return ReagentCard(soup, new List<string> { line, Loc.GetString("wg-recipe-stew-serve") });
        }
    }

    private IEnumerable<WGRecipeCard> BrewCards()
    {
        // Several recipes can brew one drink; each drink gets one card.
        foreach (var drink in _proto.EnumeratePrototypes<WGBrewRecipePrototype>().GroupBy(r => r.Drink.Id))
        {
            if (!_proto.TryIndex<ReagentPrototype>(drink.Key, out var reagent))
                continue;

            var lines = new List<string>();
            var items = drink.SelectMany(r => r.Inputs.Select(i => EntityName(i)).Concat(r.Tags.Select(t => TagName(t.Id)))).Distinct().ToList();
            if (items.Count > 0)
                lines.Add(Loc.GetString("wg-recipe-brew-items", ("items", string.Join(", ", items))));
            foreach (var powder in drink.Select(r => r.Powder).OfType<ProtoId<ReagentPrototype>>().Distinct())
            {
                if (_proto.TryIndex(powder, out var p))
                    lines.Add(Loc.GetString("wg-recipe-brew-powder", ("powder", p.LocalizedName)));
            }

            yield return ReagentCard(reagent, lines);
        }
    }

    private IEnumerable<WGRecipeCard> KitchenCards(HashSet<string> groups)
    {
        foreach (var recipe in _proto.EnumeratePrototypes<FoodRecipePrototype>())
        {
            if (recipe.HideInGuidebook || !groups.Contains(recipe.Group) || (recipe.RecipeType & BoardTypes) == 0
                || !_proto.HasIndex<EntityPrototype>(recipe.Result))
                continue;

            var items = recipe.IngredientsSolids.Select(s => Loc.GetString("wg-cook-board-count", ("name", EntityName(s.Key)), ("count", s.Value.Int())))
                .Concat(recipe.IngredientsReagents.Select(r => Loc.GetString("wg-cook-board-reagent",
                    ("name", _proto.TryIndex<ReagentPrototype>(r.Key, out var reagent) ? reagent.LocalizedName : r.Key), ("amount", r.Value.Int()))));
            var lines = new List<string> { Loc.GetString("wg-recipe-board", ("items", string.Join(", ", items))) };

            var heated = (recipe.RecipeType & (int) MicrowaveRecipeType.Assembler) == 0;
            lines.Add(Loc.GetString(heated ? "wg-recipe-then-cook" : "wg-recipe-ready"));
            if (heated && (recipe.RecipeType & HeatTypes) != 0 && recipe.IngredientsReagents.Count == 0 && recipe.IngredientsSolids.Count == 1
                && recipe.IngredientsSolids.Values.First() == 1)
                lines.Add(Loc.GetString("wg-recipe-heat-alone"));
            if (recipe.SecretRecipe)
                lines.Add(Loc.GetString("wg-recipe-secret"));

            yield return Card(recipe.Result, lines, recipe.ResultCount);
        }
    }

    /// <summary>The cooking step that turns a board dish into its finished dish, if any.</summary>
    private WGCookRecipePrototype? CookOf(EntProtoId dish)
    {
        return _proto.EnumeratePrototypes<WGCookRecipePrototype>().FirstOrDefault(r => r.Inputs.Contains(dish));
    }

    private string Counted(WGPrepIngredient ingredient)
    {
        string name;
        if (ingredient.Ids.Count > 0)
            name = ingredient.Ids.Count > 1 ? Loc.GetString("wg-cook-board-any", ("name", EntityName(ingredient.Ids[0]))) : EntityName(ingredient.Ids[0]);
        else
            name = ingredient.Tag is { } tag ? TagName(tag.Id) : Loc.GetString("wg-cook-board-something");
        return Loc.GetString("wg-cook-board-count", ("name", name), ("count", ingredient.Count));
    }

    private string EntityName(string id)
    {
        return _proto.TryIndex<EntityPrototype>(id, out var proto) ? proto.Name : id;
    }

    private static string TagName(string tag)
    {
        return Loc.TryGetString("wg-cook-tag-" + tag.ToLowerInvariant(), out var named) ? named : Loc.GetString("wg-cook-board-something");
    }

    private WGRecipeCard Card(string result, List<string> lines, int count = 1)
    {
        var name = EntityName(result);
        var title = count > 1 ? Loc.GetString("wg-cook-board-count", ("name", name), ("count", count)) : name;
        return new WGRecipeCard(title, new GuideEntityEmbed(result, false, false), lines);
    }

    private static WGRecipeCard ReagentCard(ReagentPrototype reagent, List<string> lines)
    {
        var swatch = new PanelContainer
        {
            MinSize = new System.Numerics.Vector2(24, 24),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(4),
            PanelOverride = new StyleBoxFlat { BackgroundColor = reagent.SubstanceColor },
        };
        return new WGRecipeCard(reagent.LocalizedName, swatch, lines);
    }
}

/// <summary>One recipe in the guidebook: what it makes, and how, step by step.</summary>
public sealed class WGRecipeCard : PanelContainer, ISearchableControl
{
    public readonly string Title;

    public WGRecipeCard(string title, Control icon, List<string> lines)
    {
        Title = title;
        Margin = new Thickness(5);
        PanelOverride = new StyleBoxFlat { BorderThickness = new Thickness(1), BorderColor = Color.FromHex("#777777") };

        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var header = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#393c3f") } };
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        row.AddChild(icon);
        var name = new RichTextLabel { VerticalAlignment = VAlignment.Center, Margin = new Thickness(6, 0) };
        name.SetMarkupPermissive($"[bold]{FormattedMessageEscape(title)}[/bold]");
        row.AddChild(name);
        header.AddChild(row);
        body.AddChild(header);

        var steps = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(10, 6) };
        foreach (var line in lines)
        {
            var label = new RichTextLabel();
            label.SetMarkupPermissive(line);
            steps.AddChild(label);
        }

        body.AddChild(steps);
        AddChild(body);
    }

    public bool CheckMatchesSearch(string query)
    {
        return this.ChildrenContainText(query);
    }

    public void SetHiddenState(bool state, string query)
    {
        Visible = CheckMatchesSearch(query) ? state : !state;
    }

    private static string FormattedMessageEscape(string text)
    {
        return Robust.Shared.Utility.FormattedMessage.EscapeText(text);
    }
}
