using System.Linq;
using Content.Server._WG.Farming;
using Content.Server.Nutrition.Components;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Shared._WG.Cooking;
using Content.Shared._WG.Farming;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Kitchen;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Cooking;

/// <summary>Hands-on cooking for every kitchen recipe: hearths, pans, pots and the prep board.</summary>
public sealed class WGCookingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SolutionTransferSystem _transfer = default!;
    [Dependency] private TagSystem _tags = default!;

    private const string Water = "Water";
    private const string BurnedMess = "WGFoodBurnedMess";

    /// <summary>A hearth takes this many times a kitchen machine's time for the same recipe; it cooks by hand, over a fire.</summary>
    private const float HearthTimeScale = 2f;

    /// <summary>Seconds added to every kitchen recipe on a hearth, so nothing is done in a blink.</summary>
    private const float RawDishTime = 15f;

    private const string RawDish = "WGRawKitchenDish";
    private static readonly ProtoId<TagPrototype> Knife = "Knife";
    private static readonly SoundSpecifier ChopSound = new SoundPathSpecifier("/Audio/Effects/chop.ogg");

    /// <summary>Seconds past done (or of heat, for food with no recipe) before food smokes, and before it burns.</summary>
    private const float SmokeAfter = 30f;
    private const float BurnAfter = 60f;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly SoundSpecifier Sizzle = new SoundPathSpecifier("/Audio/Effects/sizzle.ogg");
    private static readonly SoundSpecifier Bubbles = new SoundPathSpecifier("/Audio/Effects/Chemistry/bubbles.ogg");
    private static readonly SoundSpecifier Clank = new SoundPathSpecifier("/Audio/Effects/clang.ogg");
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGHearthComponent, ComponentInit>(OnHearthInit);
        SubscribeLocalEvent<WGHearthComponent, InteractUsingEvent>(OnHearthUsing);
        SubscribeLocalEvent<WGHearthComponent, InteractHandEvent>(OnHearthHand);
        SubscribeLocalEvent<WGHearthComponent, EntInsertedIntoContainerMessage>((uid, comp, args) => OnHearthContainerChanged((uid, comp), args));
        SubscribeLocalEvent<WGHearthComponent, EntRemovedFromContainerMessage>((uid, comp, args) => OnHearthContainerChanged((uid, comp), args));
        SubscribeLocalEvent<WGHearthComponent, ExaminedEvent>(OnHearthExamined);
        SubscribeLocalEvent<WGHearthComponent, ContainerIsInsertingAttemptEvent>(OnOvenInsertAttempt);
        SubscribeLocalEvent<WGPanComponent, ComponentInit>(OnPanInit);
        SubscribeLocalEvent<WGPanComponent, ExaminedEvent>(OnPanExamined);
        SubscribeLocalEvent<WGPotComponent, InteractUsingEvent>(OnPotUsing);
        SubscribeLocalEvent<WGPotComponent, ExaminedEvent>(OnPotExamined);
        SubscribeLocalEvent<WGHearthComponent, GetVerbsEvent<AlternativeVerb>>(OnHearthVerbs);
        SubscribeLocalEvent<WGHearthComponent, WGStirMessage>(OnHearthStirMessage);
        SubscribeLocalEvent<WGPrepBoardComponent, ComponentInit>(OnBoardInit);
        SubscribeLocalEvent<WGPrepBoardComponent, InteractUsingEvent>(OnBoardUsing);
        SubscribeLocalEvent<WGPrepBoardComponent, BoundUIOpenedEvent>((ent, ref _) => UpdateBoardUi(ent));
        SubscribeLocalEvent<WGPrepBoardComponent, WGPrepBoardAssembleMessage>(OnAssembleMessage);
        SubscribeLocalEvent<WGPrepBoardComponent, WGPrepBoardTakeMessage>(OnTakeMessage);
        SubscribeLocalEvent<WGPrepBoardComponent, WGPrepBoardChopMessage>(OnChopMessage);
        SubscribeLocalEvent<WGPrepBoardComponent, ExaminedEvent>(OnBoardExamined);
        SubscribeLocalEvent<WGPrepBoardComponent, GetVerbsEvent<AlternativeVerb>>(OnBoardVerbs);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => ClearRecipeCaches());
    }

    // Recipe lists sorted once; re-sorting them on every lookup stalled the server.
    private Dictionary<WGCookMethod, List<WGCookRecipePrototype>>? _cookRecipes;
    private List<WGStewRecipePrototype>? _stewRecipes;
    private List<WGStewRecipePrototype>? _comboRecipes;
    private List<WGPrepRecipePrototype>? _prepRecipes;
    private List<WGChopRecipePrototype>? _chopRecipes;
    private HashSet<string>? _comboTags;
    private readonly Dictionary<(int Types, bool Secret), List<FoodRecipePrototype>> _kitchenRecipes = new();

    /// <summary>Kitchen recipe types the prep board makes.</summary>
    private const int BoardTypes = (int) (MicrowaveRecipeType.Assembler | MicrowaveRecipeType.Oven | MicrowaveRecipeType.Microwave);

    /// <summary>How near a hearth must be for the board to offer the secret recipes it bakes.</summary>
    private const float SecretReach = 2.5f;

    /// <summary>Set while cooking swaps an item in place, so the oven takes a result it wouldn't take by hand.</summary>
    private bool _replacing;

    private void ClearRecipeCaches()
    {
        _kitchenSolids = null;
        _cookRecipes = null;
        _stewRecipes = null;
        _comboRecipes = null;
        _prepRecipes = null;
        _chopRecipes = null;
        _comboTags = null;
        _kitchenRecipes.Clear();
    }

    private List<WGPrepRecipePrototype> PrepRecipes()
    {
        return _prepRecipes ??= _proto.EnumeratePrototypes<WGPrepRecipePrototype>()
            .OrderByDescending(r => r.Ingredients.Sum(i => i.Count))
            .ThenBy(r => r.ID)
            .ToList();
    }

    private void OnHearthInit(Entity<WGHearthComponent> ent, ref ComponentInit args)
    {
        _containers.EnsureContainer<ContainerSlot>(ent, WGHearthComponent.Hob);
        _containers.EnsureContainer<Container>(ent, WGHearthComponent.Oven);
    }

    private void OnPanInit(Entity<WGPanComponent> ent, ref ComponentInit args)
    {
        _containers.EnsureContainer<Container>(ent, WGPanComponent.Contents);
    }

    private void OnBoardInit(Entity<WGPrepBoardComponent> ent, ref ComponentInit args)
    {
        _containers.EnsureContainer<Container>(ent, WGPrepBoardComponent.Contents);
    }

    #region Recipes

    private bool Matches(EntityUid item, List<EntProtoId> inputs, List<ProtoId<TagPrototype>> tags)
    {
        var id = MetaData(item).EntityPrototype?.ID;
        return id != null && inputs.Any(i => i.Id == id) || tags.Count > 0 && _tags.HasAnyTag(item, tags);
    }

    public WGCookRecipePrototype? CookRecipe(EntityUid item, WGCookMethod method)
    {
        _cookRecipes ??= _proto.EnumeratePrototypes<WGCookRecipePrototype>()
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Inputs.Count == 0)
            .ThenBy(r => r.ID)
            .GroupBy(r => r.Method)
            .ToDictionary(g => g.Key, g => g.ToList());
        return _cookRecipes.TryGetValue(method, out var recipes) ? recipes.FirstOrDefault(r => Matches(item, r.Inputs, r.Tags)) : null;
    }

    public WGStewRecipePrototype? StewRecipe(EntityUid item)
    {
        _stewRecipes ??= _proto.EnumeratePrototypes<WGStewRecipePrototype>()
            .Where(r => r.Combine.Count == 0)
            .OrderBy(r => r.Order)
            .ThenBy(r => r.ID)
            .ToList();
        return _stewRecipes.FirstOrDefault(r => Matches(item, r.Inputs, r.Tags));
    }

    #endregion

    #region Hearth

    /// <summary>Whether a hearth is burning: anchored and powered.</summary>
    public bool Lit(EntityUid uid)
    {
        // Anchored, or a camp stove set down on the ground rather than carried or packed away.
        var placed = Transform(uid).Anchored
            || TryComp<WGHearthComponent>(uid, out var hearth) && !hearth.NeedsAnchor && !_containers.IsEntityInContainer(uid);
        return placed && _power.IsPowered(uid);
    }

    /// <summary>Whether a hearth gives off a fire's warmth: lit and with a hob, so not a microwave or food-o-mat.</summary>
    public bool Warms(Entity<WGHearthComponent> hearth)
    {
        return hearth.Comp.HasHob && Lit(hearth);
    }

    private EntityUid? HobItem(EntityUid hearth)
    {
        return _containers.TryGetContainer(hearth, WGHearthComponent.Hob, out var hob) && hob.ContainedEntities.Count > 0 ? hob.ContainedEntities[0] : null;
    }

    private void UpdateHob(EntityUid hearth)
    {
        var item = HobItem(hearth);
        var kind = item == null ? WGHobCookware.None : HasComp<WGPanComponent>(item) ? WGHobCookware.Pan : WGHobCookware.Pot;
        _appearance.SetData(hearth, WGHobVisuals.Cookware, kind);
    }

    /// <summary>Whether the oven takes an item: anything that bakes, any food, or an unbaked dish this hearth can cook.</summary>
    private bool Ovenable(EntityUid hearth, EntityUid item)
    {
        return (CookRecipe(item, WGCookMethod.Bake) != null || HasComp<FoodComponent>(item) || HasComp<WGRawDishComponent>(item))
            && CanCook(hearth, item);
    }

    /// <summary>Keeps what the oven can't cook out of it, however it is put in, such as by an automation arm.</summary>
    private void OnOvenInsertAttempt(Entity<WGHearthComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID == WGHearthComponent.Oven && !_replacing && !Ovenable(ent, args.EntityUid))
            args.Cancel();
    }

    private void OnHearthUsing(Entity<WGHearthComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        var used = args.Used;
        var hob = _containers.GetContainer(ent, WGHearthComponent.Hob);
        var onHob = HobItem(ent);

        if (!ent.Comp.HasHob && ent.Comp.OvenCapacity <= 0)
            return;

        // Cookware goes on the hob.
        if (ent.Comp.HasHob && (HasComp<WGPanComponent>(used) || HasComp<WGPotComponent>(used)))
        {
            args.Handled = true;
            if (onHob != null)
            {
                _popup.PopupEntity(Loc.GetString("wg-cook-hob-full"), ent, args.User);
                return;
            }

            if (!_containers.Insert(used, hob))
                return;
            _audio.PlayPvs(Clank, ent);
            _popup.PopupEntity(Loc.GetString("wg-cook-hob-set", ("item", used)), ent, args.User);
            return;
        }

        // A secret dish goes only to a hearth that provides its recipe.
        if (!CanCook(ent, used))
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("wg-cook-hearth-cant-cook", ("item", used)), ent, args.User);
            return;
        }

        // A pan on the hob takes what can be fried; a pot takes what can be stewed.
        if (onHob is { } cookware && TryComp<WGPanComponent>(cookware, out var pan)
            && (CookRecipe(used, WGCookMethod.Fry) != null || IsFryable(used)))
        {
            args.Handled = true;
            AddToPan((cookware, pan), used, args.User);
            return;
        }

        if (onHob is { } pot && TryComp<WGPotComponent>(pot, out var potComp) && StewRecipe(used) != null)
        {
            args.Handled = true;
            AddToPot((pot, potComp), used, args.User);
            return;
        }

        // A bowl, jug or ladle reaches the pot on the hob: pours in what it holds, or ladles soup out if empty.
        if (onHob is { } ladled && HasComp<WGPotComponent>(ladled) && TryComp<SolutionTransferComponent>(used, out var transfer))
        {
            args.Handled = true;
            Ladle(ladled, (used, transfer), args.User);
            return;
        }

        if (ent.Comp.OvenCapacity <= 0 || !Ovenable(ent, used))
            return;

        args.Handled = true;
        var oven = _containers.GetContainer(ent, WGHearthComponent.Oven);
        if (oven.ContainedEntities.Count >= ent.Comp.OvenCapacity)
        {
            _popup.PopupEntity(Loc.GetString("wg-cook-oven-full"), ent, args.User);
            return;
        }

        if (_containers.Insert(used, oven))
            _popup.PopupEntity(Loc.GetString("wg-cook-oven-in", ("item", used)), ent, args.User);
    }

    /// <summary>Pours a container into the pot on the hob, or ladles soup (before water) into it if empty.</summary>
    private void Ladle(EntityUid pot, Entity<SolutionTransferComponent> container, EntityUid user)
    {
        if (!_solutions.TryGetSolution(pot, Comp<WGPotComponent>(pot).Solution, out var potSoln, out var potSolution))
            return;

        if (_solutions.TryGetDrainableSolution(container.Owner, out var fromSoln, out var from) && from.Volume > 0)
        {
            if (container.Comp.CanSend
                && _transfer.Transfer(user, container, fromSoln.Value, pot, potSoln.Value, container.Comp.TransferAmount) is var poured
                && poured > FixedPoint2.Zero)
                _popup.PopupEntity(Loc.GetString("wg-cook-pot-poured", ("amount", poured), ("item", container.Owner)), Shown(pot), user);
            return;
        }

        if (!container.Comp.CanReceive || !_solutions.TryGetRefillableSolution(container.Owner, out var intoSoln, out var into))
            return;

        var amount = FixedPoint2.Min(container.Comp.TransferAmount, into.AvailableVolume);
        if (amount <= FixedPoint2.Zero || potSolution.Volume <= FixedPoint2.Zero)
            return;

        var soup = _solutions.SplitSolutionWithout(potSoln.Value, amount, Water);
        if (soup.Volume < amount)
            soup.AddSolution(_solutions.SplitSolution(potSoln.Value, amount - soup.Volume), _proto);
        _solutions.TryAddSolution(intoSoln.Value, soup);
        _audio.PlayPvs(Bubbles, Shown(pot), AudioParams.Default.WithVolume(-6f));
        _popup.PopupEntity(Loc.GetString("wg-cook-pot-ladled", ("amount", soup.Volume), ("item", container.Owner)), Shown(pot), user);
    }

    private void OnHearthHand(Entity<WGHearthComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        // Take food out of the pan first, then out of the oven, then the cookware itself.
        EntityUid? take = null;
        if (HobItem(ent) is { } cookware && TryComp<WGPanComponent>(cookware, out _)
            && _containers.GetContainer(cookware, WGPanComponent.Contents).ContainedEntities.LastOrDefault() is { Valid: true } fried)
            take = fried;
        else if (_containers.GetContainer(ent, WGHearthComponent.Oven).ContainedEntities.LastOrDefault() is { Valid: true } baked)
            take = baked;
        else if (HobItem(ent) is { } item)
            take = item;

        if (take is not { } taken)
            return;

        // Heat is kept when taken out, so lifting food out neither resets nor saves it.
        args.Handled = true;
        _hands.PickupOrDrop(args.User, taken);
    }

    /// <summary>Keeps the hob's sprite right however the cookware comes or goes.</summary>
    private void OnHearthContainerChanged(Entity<WGHearthComponent> ent, ContainerModifiedMessage args)
    {
        if (args.Container.ID == WGHearthComponent.Hob)
            UpdateHob(ent);
    }

    private void OnHearthExamined(Entity<WGHearthComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(Lit(ent) ? "wg-cook-hearth-lit" : "wg-cook-hearth-cold"));
        if (HobItem(ent) is { } item)
            args.PushMarkup(Loc.GetString("wg-cook-hearth-hob", ("item", item)));
        foreach (var baked in _containers.GetContainer(ent, WGHearthComponent.Oven).ContainedEntities)
            args.PushMarkup(Loc.GetString("wg-cook-hearth-oven", ("item", baked), ("state", Doneness(baked))));
    }

    #endregion

    #region Pan and pot

    private void AddToPan(Entity<WGPanComponent> pan, EntityUid food, EntityUid user)
    {
        var contents = _containers.GetContainer(pan, WGPanComponent.Contents);
        if (contents.ContainedEntities.Count >= pan.Comp.Capacity)
        {
            _popup.PopupEntity(Loc.GetString("wg-cook-pan-full"), Shown(pan), user);
            return;
        }

        if (!_containers.Insert(food, contents))
            return;
        _audio.PlayPvs(Sizzle, Shown(pan));
        _popup.PopupEntity(Loc.GetString("wg-cook-pan-in", ("item", food)), Shown(pan), user);
    }

    private void OnPotUsing(Entity<WGPotComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || StewRecipe(args.Used) == null)
            return;

        args.Handled = true;
        AddToPot(ent, args.Used, args.User);
    }

    private void AddToPot(Entity<WGPotComponent> pot, EntityUid food, EntityUid user)
    {
        if (StewRecipe(food) is not { } recipe || !_solutions.TryGetSolution(pot.Owner, pot.Comp.Solution, out _, out var solution))
            return;

        if (pot.Comp.Boil < pot.Comp.BoilAt)
        {
            _popup.PopupEntity(Loc.GetString("wg-cook-pot-not-boiling"), Shown(pot), user);
            return;
        }

        var needed = pot.Comp.Portion * (pot.Comp.Jobs.Sum(j => j.Portions) + 1);
        if (solution.GetTotalPrototypeQuantity(Water) < FixedPoint2.New(needed))
        {
            _popup.PopupEntity(Loc.GetString("wg-cook-pot-no-water"), Shown(pot), user);
            return;
        }

        pot.Comp.Jobs.Add(new WGStewJob
        {
            Recipe = recipe.ID,
            Left = recipe.Seconds,
            Ingredient = MetaData(food).EntityPrototype?.ID ?? string.Empty,
            Tags = ComboTags().Where(t => _tags.HasTag(food, t)).ToHashSet(),
        });
        _popup.PopupEntity(Loc.GetString("wg-cook-pot-in", ("item", food)), Shown(pot), user);
        _audio.PlayPvs(Bubbles, Shown(pot));
        QueueDel(food);
        Combine(pot, user);
    }

    /// <summary>Combines stewing ingredients into the biggest multi-ingredient stew they make.</summary>
    private void Combine(Entity<WGPotComponent> pot, EntityUid user)
    {
        _comboRecipes ??= _proto.EnumeratePrototypes<WGStewRecipePrototype>()
            .Where(r => r.Combine.Count > 0)
            .OrderByDescending(r => r.Combine.Sum(i => i.Count))
            .ThenBy(r => r.ID)
            .ToList();
        foreach (var recipe in _comboRecipes)
        {
            var free = pot.Comp.Jobs.Where(j => j.Ingredient.Length > 0).ToList();
            var used = new List<WGStewJob>();
            var ok = true;
            foreach (var ingredient in recipe.Combine)
            {
                var picks = free.Where(j => ingredient.Ids.Any(i => i.Id == j.Ingredient)
                        || ingredient.Tag is { } tag && j.Tags.Contains(tag))
                    .Take(ingredient.Count).ToList();
                if (picks.Count < ingredient.Count)
                {
                    ok = false;
                    break;
                }

                used.AddRange(picks);
                free.RemoveAll(picks.Contains);
            }

            if (!ok)
                continue;

            pot.Comp.Jobs.RemoveAll(used.Contains);
            pot.Comp.Jobs.Add(new WGStewJob { Recipe = recipe.ID, Left = recipe.Seconds, Portions = used.Count });
            _popup.PopupEntity(Loc.GetString("wg-cook-stew-combine"), Shown(pot), user, PopupType.Medium);
            return;
        }
    }

    /// <summary>Every tag a stew of several ingredients asks for.</summary>
    private HashSet<string> ComboTags()
    {
        return _comboTags ??= _proto.EnumeratePrototypes<WGStewRecipePrototype>()
            .SelectMany(r => r.Combine)
            .Where(i => i.Tag != null)
            .Select(i => i.Tag!.Value.Id)
            .ToHashSet();
    }

    /// <summary>A stir counts once in this long.</summary>
    public static readonly TimeSpan StirEvery = TimeSpan.FromSeconds(15);

    /// <summary>Seconds a perfect stir takes off every stew in the pot.</summary>
    private const float StirSpeedUp = 12f;

    /// <summary>Extra share of soup a well-stirred portion comes out with.</summary>
    private const float StirBonus = 0.25f;

    private void OnHearthVerbs(Entity<WGHearthComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || HobItem(ent) is not { } pot || !HasComp<WGPotComponent>(pot))
            return;

        var user = args.User;
        var potComp = Comp<WGPotComponent>(pot);
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wg-stir-verb"),
            // Opened on the hearth: the UI range check can't reach the pot inside it.
            Act = () =>
            {
                if (StirBlocked((pot, potComp)) is { } why)
                    _popup.PopupEntity(Loc.GetString(why), ent, user);
                else
                    _ui.OpenUi(ent.Owner, WGStirUiKey.Key, user);
            },
            Priority = 3,
        });
    }

    /// <summary>Why the pot can't be stirred now, as a loc id, or null.</summary>
    private string? StirBlocked(Entity<WGPotComponent> pot)
    {
        if (pot.Comp.Jobs.Count == 0)
            return "wg-stir-nothing";
        return _timing.CurTime - pot.Comp.LastStir < StirEvery ? "wg-stir-too-soon" : null;
    }

    private void OnHearthStirMessage(Entity<WGHearthComponent> ent, ref WGStirMessage args)
    {
        if (HobItem(ent) is { } pot && TryComp<WGPotComponent>(pot, out var potComp))
            Stir((pot, potComp), args.Actor, args.Score);
    }

    /// <summary>Stirs the pot as the stirring minigame scored it.</summary>
    public bool Stir(Entity<WGPotComponent> ent, EntityUid user, float score)
    {
        score = Math.Clamp(score, 0f, 1f);
        if (StirBlocked(ent) is { } why)
        {
            _popup.PopupEntity(Loc.GetString(why), Shown(ent), user);
            return false;
        }

        ent.Comp.LastStir = _timing.CurTime;
        foreach (var job in ent.Comp.Jobs)
            job.Left -= StirSpeedUp * score;
        ent.Comp.Stirred += score;
        _audio.PlayPvs(Bubbles, Shown(ent));
        _popup.PopupEntity(Loc.GetString(score >= 0.75f ? "wg-stir-good" : score >= 0.4f ? "wg-stir-fair" : "wg-stir-poor"), Shown(ent), user);
        return true;
    }

    /// <summary>Where to show a popup or play a sound for something that may sit inside a hearth or a pan: its holder.</summary>
    private EntityUid Shown(EntityUid uid)
    {
        while (_containers.TryGetContainingContainer(uid, out var container))
            uid = container.Owner;
        return uid;
    }

    private void OnPanExamined(Entity<WGPanComponent> ent, ref ExaminedEvent args)
    {
        foreach (var food in _containers.GetContainer(ent, WGPanComponent.Contents).ContainedEntities)
            args.PushMarkup(Loc.GetString("wg-cook-pan-holds", ("item", food), ("state", Doneness(food))));
    }

    private void OnPotExamined(Entity<WGPotComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString(ent.Comp.Boil >= ent.Comp.BoilAt ? "wg-cook-pot-boiling" : "wg-cook-pot-still"));
        if (ent.Comp.Jobs.Count > 0)
        {
            args.PushMarkup(Loc.GetString("wg-cook-pot-stewing", ("count", ent.Comp.Jobs.Count)));
            if (_solutions.TryGetSolution(ent.Owner, ent.Comp.Solution, out _, out var solution)
                && solution.GetTotalPrototypeQuantity(Water) <= FixedPoint2.Zero)
                args.PushMarkup(Loc.GetString("wg-cook-pot-dry"));
        }
        foreach (var job in ent.Comp.Jobs)
        {
            if (_proto.TryIndex<WGStewRecipePrototype>(job.Recipe, out var recipe) && _proto.TryIndex<ReagentPrototype>(recipe.Soup, out var soup))
                args.PushMarkup(Loc.GetString("wg-cook-pot-becoming", ("soup", soup.LocalizedName)));
        }
    }

    private string Doneness(EntityUid item)
    {
        if (!TryComp<WGHeatingComponent>(item, out var heat))
            return Loc.GetString("wg-cook-state-raw");
        if (heat.Smoking)
            return Loc.GetString("wg-cook-state-smoking");
        return Loc.GetString(heat.Done ? "wg-cook-state-done" : "wg-cook-state-cooking");
    }

    #endregion

    #region Heat

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;
        _next = _timing.CurTime + Tick;
        var seconds = (float) Tick.TotalSeconds;

        var query = EntityQueryEnumerator<WGHearthComponent>();
        while (query.MoveNext(out var uid, out var hearth))
        {
            var lit = Lit(uid);
            _appearance.SetData(uid, WGHobVisuals.Lit, lit);
            if (!lit)
                continue;

            var heat = seconds * hearth.Heat;
            if (HobItem(uid) is { } cookware)
            {
                if (TryComp<WGPanComponent>(cookware, out _))
                {
                    var foods = _containers.GetContainer(cookware, WGPanComponent.Contents).ContainedEntities.ToList();
                    foreach (var food in foods)
                        Cook(food, WGCookMethod.Fry, heat, uid);

                    hearth.SizzleTimer -= seconds;
                    if (foods.Count > 0 && hearth.SizzleTimer <= 0)
                    {
                        hearth.SizzleTimer = 6f;
                        _audio.PlayPvs(Sizzle, uid, AudioParams.Default.WithVolume(-4f));
                    }
                }
                else if (TryComp<WGPotComponent>(cookware, out var pot))
                {
                    Stew((cookware, pot), heat);
                }
            }

            foreach (var food in _containers.GetContainer(uid, WGHearthComponent.Oven).ContainedEntities.ToList())
                Cook(food, WGCookMethod.Bake, heat, uid);
        }

        // Pots off a lit hob cool down.
        var pots = EntityQueryEnumerator<WGPotComponent>();
        while (pots.MoveNext(out var uid, out var pot))
        {
            if (_containers.TryGetContainingContainer(uid, out var holder) && holder.ID == WGHearthComponent.Hob && Lit(holder.Owner))
                continue;
            pot.Boil = Math.Max(0f, pot.Boil - 2f * seconds);
        }
    }

    /// <summary>Whether an unbaked kitchen dish is a microwave recipe, which a pan cooks; oven recipes go in the oven.</summary>
    private bool IsFryable(EntityUid item)
    {
        return TryComp<WGRawDishComponent>(item, out var raw) && _proto.TryIndex<FoodRecipePrototype>(raw.Recipe, out var recipe)
            && (recipe.RecipeType & (int) MicrowaveRecipeType.Microwave) != 0 && (recipe.RecipeType & (int) MicrowaveRecipeType.Oven) == 0;
    }

    /// <summary>The kitchen's oven and microwave recipes, most ingredients first, as the kitchen machines try them.</summary>
    /// <param name="secret">Also secret recipes, which only a hearth whose FoodRecipeProvider lists them bakes.</param>
    private List<FoodRecipePrototype> KitchenRecipes(int types, bool secret = false)
    {
        if (_kitchenRecipes.TryGetValue((types, secret), out var cached))
            return cached;
        return _kitchenRecipes[(types, secret)] = _proto.EnumeratePrototypes<FoodRecipePrototype>()
            .Where(r => (secret || !r.SecretRecipe) && (r.RecipeType & types) != 0)
            .OrderByDescending(r => r.IngredientCount())
            .ThenBy(r => r.ID)
            .ToList();
    }

    /// <summary>The solids a kitchen recipe would use from the items, the first of each prototype; null if any are short.</summary>
    private HashSet<EntityUid>? PickSolids(IReadOnlyList<EntityUid> items, FoodRecipePrototype recipe)
    {
        if (recipe.IngredientsSolids.Count == 0)
            return null;

        var picked = new HashSet<EntityUid>();
        foreach (var (id, count) in recipe.IngredientsSolids)
        {
            var matches = items.Where(e => MetaData(e).EntityPrototype?.ID == id).Take(count.Int()).ToList();
            if (matches.Count < count.Int())
                return null;
            picked.UnionWith(matches);
        }

        return picked;
    }

    /// <summary>Whether the items hold what a kitchen recipe needs: its solids, and its reagents outside those solids.</summary>
    private bool Holds(IReadOnlyList<EntityUid> items, FoodRecipePrototype recipe)
    {
        if (PickSolids(items, recipe) is not { } solids)
            return false;

        // Reagents inside a solid don't count: the solid is used up whole, as Consume does.
        foreach (var (reagent, amount) in recipe.IngredientsReagents)
        {
            var have = FixedPoint2.Zero;
            foreach (var item in items)
            {
                if (solids.Contains(item) || !TryComp<SolutionContainerManagerComponent>(item, out var manager))
                    continue;
                foreach (var (_, soln) in _solutions.EnumerateSolutions((item, manager)))
                    have += soln.Comp.Solution.GetTotalPrototypeQuantity(reagent);
            }

            if (have < amount)
                return false;
        }

        return true;
    }

    /// <summary>Uses up a kitchen recipe's ingredients: the picked solids whole, and its reagents from the other items.</summary>
    private void Consume(IReadOnlyList<EntityUid> items, FoodRecipePrototype recipe, HashSet<EntityUid> solids)
    {
        foreach (var (reagent, amount) in recipe.IngredientsReagents)
        {
            var left = amount;
            foreach (var item in items)
            {
                if (left <= FixedPoint2.Zero || solids.Contains(item) || !TryComp<SolutionContainerManagerComponent>(item, out var manager))
                    continue;
                foreach (var (_, soln) in _solutions.EnumerateSolutions((item, manager)))
                {
                    var take = FixedPoint2.Min(left, soln.Comp.Solution.GetTotalPrototypeQuantity(reagent));
                    if (take <= FixedPoint2.Zero)
                        continue;
                    _solutions.RemoveReagent(soln, reagent, take);
                    left -= take;
                }
            }
        }

        foreach (var item in solids)
            Del(item);
    }

    /// <summary>Heats one item: it turns into its recipe's result when done, then smokes, then burns.</summary>
    private void Cook(EntityUid food, WGCookMethod method, float heat, EntityUid hearth)
    {
        // Already burned; it can't burn again, or it would warn the cook every minute.
        if (MetaData(food).EntityPrototype?.ID == BurnedMess)
            return;

        var state = EnsureComp<WGHeatingComponent>(food);
        state.Heat += heat;

        // An unbaked kitchen dish cooks into its recipe's result, in the oven or the pan.
        if (!state.Done && TryComp<WGRawDishComponent>(food, out var raw) && _proto.TryIndex<FoodRecipePrototype>(raw.Recipe, out var kitchen))
        {
            // A hearth refuses a secret dish it can't cook; one carried over in a pan waits for the right hearth.
            if (!CanCook(hearth, food))
            {
                state.Heat -= heat;
                return;
            }

            if (state.Heat < kitchen.CookTime * HearthTimeScale + RawDishTime)
                return;

            var container = _containers.TryGetContainingContainer(food, out var holder) ? holder : null;
            var rawQuality = CompOrNull<WGProduceQualityComponent>(food)?.Quality;
            var dish = Replace(food, kitchen.Result);
            EnsureComp<WGHeatingComponent>(dish).Done = true;
            for (var i = 1; i < kitchen.ResultCount; i++)
            {
                var extra = container != null ? SpawnCooked(kitchen.Result, container) : Spawn(kitchen.Result, Transform(dish).Coordinates);
                EnsureComp<WGHeatingComponent>(extra).Done = true;
                SetQuality(extra, rawQuality);
            }

            _popup.PopupEntity(Loc.GetString("wg-cook-done", ("item", dish)), hearth, PopupType.Medium);
            return;
        }

        if (!state.Done && CookRecipe(food, method) is { } recipe)
        {
            if (state.Heat < recipe.Seconds)
                return;

            var cooked = Replace(food, recipe.Result);
            var done = EnsureComp<WGHeatingComponent>(cooked);
            done.Done = true;
            _popup.PopupEntity(Loc.GetString("wg-cook-done", ("item", cooked)), hearth, PopupType.Medium);
            return;
        }

        if (!HasComp<FoodComponent>(food))
            return;

        if (!state.Smoking && state.Heat >= SmokeAfter)
        {
            state.Smoking = true;
            _popup.PopupEntity(Loc.GetString("wg-cook-smoking", ("item", food)), hearth, PopupType.MediumCaution);
        }

        if (state.Heat >= BurnAfter)
        {
            Replace(food, BurnedMess, keepQuality: false);
            _popup.PopupEntity(Loc.GetString("wg-cook-burned"), hearth, PopupType.LargeCaution);
        }
    }

    /// <summary>The secret recipes a hearth bakes, asked the way the kitchen machines ask.</summary>
    private IEnumerable<string> SecretRecipes(EntityUid hearth)
    {
        var ev = new GetSecretRecipesEvent();
        RaiseLocalEvent(hearth, ref ev);
        return ev.Recipes.Select(r => r.ID);
    }

    /// <summary>Whether a hearth can cook this item: anything but a secret dish it doesn't provide.</summary>
    private bool CanCook(EntityUid hearth, EntityUid item)
    {
        return !TryComp<WGRawDishComponent>(item, out var raw) || !_proto.TryIndex<FoodRecipePrototype>(raw.Recipe, out var recipe)
            || !recipe.SecretRecipe || SecretRecipes(hearth).Contains(recipe.ID);
    }

    /// <summary>The secret recipes the hearths within reach of a board bake, which the board may then offer.</summary>
    private HashSet<string> SecretsNear(EntityUid board)
    {
        var secrets = new HashSet<string>();
        foreach (var hearth in _lookup.GetEntitiesInRange<WGHearthComponent>(Transform(board).Coordinates, SecretReach))
            secrets.UnionWith(SecretRecipes(hearth));
        return secrets;
    }

    /// <summary>The kitchen recipes a board offers: the public ones, and the secret ones a hearth nearby bakes.</summary>
    private IEnumerable<FoodRecipePrototype> BoardRecipes(EntityUid board)
    {
        var secrets = SecretsNear(board);
        return KitchenRecipes(BoardTypes, secret: secrets.Count > 0).Where(r => !r.SecretRecipe || secrets.Contains(r.ID));
    }

    /// <summary>Swaps an item for another in its container, keeping its quality.</summary>
    private EntityUid Replace(EntityUid item, EntProtoId result, bool keepQuality = true)
    {
        var quality = keepQuality ? CompOrNull<WGProduceQualityComponent>(item)?.Quality : null;
        var made = ReplaceEntity(item, result);
        SetQuality(made, quality);
        return made;
    }

    /// <summary>Gives an item a quality, if better than normal.</summary>
    private void SetQuality(EntityUid item, WGCropQuality? quality)
    {
        if (quality is { } q && q != WGCropQuality.Normal)
            EnsureComp<WGProduceQualityComponent>(item).Quality = q;
    }

    /// <summary>The quality a dish made of these ingredients has: their average, rounded down.</summary>
    private WGCropQuality? Average(IEnumerable<EntityUid> ingredients)
    {
        var total = 0;
        var count = 0;
        foreach (var item in ingredients)
        {
            total += (int) (CompOrNull<WGProduceQualityComponent>(item)?.Quality ?? WGCropQuality.Normal);
            count++;
        }

        return count == 0 ? null : (WGCropQuality) (total / count);
    }

    private EntityUid SpawnCooked(EntProtoId result, BaseContainer container)
    {
        return SpawnCooked(result, container.Owner, container.ID);
    }

    /// <summary>Spawns what cooking made into the container it was made in, which the oven takes whatever it is.</summary>
    private EntityUid SpawnCooked(EntProtoId result, EntityUid owner, string container)
    {
        _replacing = true;
        try
        {
            return SpawnInContainerOrDrop(result, owner, container);
        }
        finally
        {
            _replacing = false;
        }
    }

    private EntityUid ReplaceEntity(EntityUid item, EntProtoId result)
    {
        EntityUid made;
        if (_containers.TryGetContainingContainer(item, out var container))
        {
            var owner = container.Owner;
            var id = container.ID;
            _containers.Remove(item, container);
            Del(item);
            made = SpawnCooked(result, owner, id);
        }
        else
        {
            made = Spawn(result, Transform(item).Coordinates);
            Del(item);
        }

        return made;
    }

    private void Stew(Entity<WGPotComponent> pot, float heat)
    {
        if (!_solutions.TryGetSolution(pot.Owner, pot.Comp.Solution, out var soln, out var solution))
            return;

        if (solution.GetTotalPrototypeQuantity(Water) <= FixedPoint2.Zero)
        {
            // Stews still to finish wait in the pot for more water.
            if (pot.Comp.Boil > 0 && pot.Comp.Jobs.Count > 0)
                _popup.PopupEntity(Loc.GetString("wg-cook-pot-dry"), Shown(pot), PopupType.MediumCaution);
            pot.Comp.Boil = 0;
            return;
        }

        var wasBoiling = pot.Comp.Boil >= pot.Comp.BoilAt;
        pot.Comp.Boil = Math.Min(pot.Comp.BoilAt + 5f, pot.Comp.Boil + heat);
        if (!wasBoiling && pot.Comp.Boil >= pot.Comp.BoilAt)
        {
            _popup.PopupEntity(Loc.GetString("wg-cook-pot-boils"), Shown(pot), PopupType.Medium);
            _audio.PlayPvs(Bubbles, Shown(pot));
        }

        if (pot.Comp.Boil < pot.Comp.BoilAt)
            return;

        foreach (var job in pot.Comp.Jobs.ToList())
        {
            job.Left -= heat;
            // Only the water still in the pot turns into soup: ladled out early, it makes less, and with none left the
            // stew waits for more.
            var water = solution.GetTotalPrototypeQuantity(Water);
            if (job.Left > 0 || water <= FixedPoint2.Zero)
                continue;

            pot.Comp.Jobs.Remove(job);
            if (!_proto.TryIndex<WGStewRecipePrototype>(job.Recipe, out var recipe))
                continue;

            var portion = FixedPoint2.Min(FixedPoint2.New(pot.Comp.Portion * job.Portions), water);
            _solutions.RemoveReagent(soln.Value, Water, portion);
            // A well-stirred pot gives a bigger portion.
            var made = pot.Comp.Stirred >= 1f ? portion * (1f + StirBonus) : portion;
            pot.Comp.Stirred = 0;
            _solutions.TryAddReagent(soln.Value, recipe.Soup, made);
            _popup.PopupEntity(Loc.GetString("wg-cook-stew-done"), Shown(pot), PopupType.Medium);
            _audio.PlayPvs(Bubbles, Shown(pot));
        }
    }

    #endregion

    #region Prep board

    private void OnBoardUsing(Entity<WGPrepBoardComponent> ent, ref InteractUsingEvent args)
    {
        var used = args.Used;
        if (!args.Handled && _tags.HasTag(used, Knife))
        {
            args.Handled = true;
            _ui.OpenUi(ent.Owner, WGPrepBoardUiKey.Key, args.User);
            return;
        }

        // Cookware goes on the hob, not the board.
        if (args.Handled || HasComp<WGPanComponent>(used) || HasComp<WGPotComponent>(used)
            || !HasComp<FoodComponent>(used) && !HasComp<SolutionContainerManagerComponent>(used)
            && !KitchenSolids().Contains(MetaData(used).EntityPrototype?.ID ?? string.Empty)
            && !PrepRecipes().Any(r => r.Ingredients.Any(i => Fits(used, i))))
            return;

        args.Handled = true;
        var contents = _containers.GetContainer(ent, WGPrepBoardComponent.Contents);
        if (contents.ContainedEntities.Count >= ent.Comp.Capacity)
        {
            _popup.PopupEntity(Loc.GetString("wg-cook-board-full"), ent, args.User);
            return;
        }

        if (!_containers.Insert(args.Used, contents))
            return;
        _popup.PopupEntity(Loc.GetString("wg-cook-board-in", ("item", args.Used)), ent, args.User);
        UpdateBoardUi(ent);
    }

    private WGChopRecipePrototype? ChopRecipe(EntityUid item)
    {
        _chopRecipes ??= _proto.EnumeratePrototypes<WGChopRecipePrototype>().OrderBy(r => r.ID).ToList();
        return _chopRecipes.FirstOrDefault(r => Matches(item, r.Inputs, r.Tags));
    }

    /// <summary>The ways an item can be cut on the board, each what it makes and how many.</summary>
    public List<(EntProtoId Result, int Count)> ChopOptions(EntityUid item)
    {
        var options = new List<(EntProtoId Result, int Count)>();
        if (ChopRecipe(item) is { } recipe)
            options.Add((recipe.Result, recipe.Count));
        if (TryComp<SliceableFoodComponent>(item, out var slice) && slice.Slice is { } piece && options.All(o => o.Result != piece))
            options.Add((piece, Math.Max(1, (int) slice.TotalCount)));
        return options;
    }

    /// <summary>The first way <paramref name="item"/> can be cut on the board, if any.</summary>
    public bool TryGetChop(EntityUid item, out EntProtoId result, out int count)
    {
        var options = ChopOptions(item);
        (result, count) = options.Count > 0 ? options[0] : (default, 0);
        return options.Count > 0;
    }

    /// <summary>Chops an item on the board by <paramref name="option"/> of <see cref="ChopOptions"/>, scored by the minigame.</summary>
    public bool ChopItem(Entity<WGPrepBoardComponent> board, EntityUid item, EntityUid user, float score, int option = 0)
    {
        var contents = _containers.GetContainer(board, WGPrepBoardComponent.Contents);
        var options = ChopOptions(item);
        if (!contents.Contains(item) || option < 0 || option >= options.Count)
            return false;
        var (result, pieces) = options[option];

        if (!_hands.EnumerateHeld(user).Any(held => _tags.HasTag(held, Knife)))
        {
            _popup.PopupEntity(Loc.GetString("wg-cook-board-need-knife"), board, user);
            return false;
        }

        score = Math.Clamp(score, 0f, 1f);
        var count = score >= 0.5f ? pieces : Math.Max(1, pieces - 1);
        var name = Name(item);
        var quality = CompOrNull<WGProduceQualityComponent>(item)?.Quality;
        // The pieces share what is left in the item, as slicing does: a half-eaten or poisoned item gives the same.
        Solution? left = null;
        var trash = new List<EntProtoId>();
        if (TryComp<FoodComponent>(item, out var food))
        {
            trash.AddRange(food.Trash);
            if (_solutions.TryGetSolution(item, food.Solution, out var soln, out _))
                left = _solutions.SplitSolution(soln.Value, soln.Value.Comp.Solution.Volume);
        }

        _containers.Remove(item, contents);
        Del(item);
        var share = left == null ? FixedPoint2.Zero : left.Volume / FixedPoint2.New(count);
        for (var i = 0; i < count; i++)
        {
            var piece = SpawnInContainerOrDrop(result, board, WGPrepBoardComponent.Contents);
            SetQuality(piece, quality);
            if (left != null)
                FillPiece(piece, left.SplitSolution(share));
        }

        foreach (var junk in trash)
            Spawn(junk, Transform(board).Coordinates);

        _audio.PlayPvs(ChopSound, board);
        var key = score >= 0.85f ? "wg-cook-board-chopped-clean" : score >= 0.5f ? "wg-cook-board-chopped" : "wg-cook-board-chopped-sloppy";
        _popup.PopupEntity(Loc.GetString(key, ("item", name), ("count", count)), board, user);
        UpdateBoardUi(board);
        return true;
    }

    /// <summary>Replaces a chopped piece's own reagents with its share of the item's, as far as it holds.</summary>
    private void FillPiece(EntityUid piece, Solution share)
    {
        if (!TryComp<FoodComponent>(piece, out var food) || !_solutions.TryGetSolution(piece, food.Solution, out var soln, out var solution))
            return;

        _solutions.RemoveAllSolution(soln.Value);
        _solutions.TryAddSolution(soln.Value, share.SplitSolution(solution.AvailableVolume));
    }

    private void OnChopMessage(Entity<WGPrepBoardComponent> ent, ref WGPrepBoardChopMessage args)
    {
        if (TryGetEntity(args.Item, out var item))
            ChopItem(ent, item.Value, args.Actor, args.Score, args.Option);
    }

    private void OnAssembleMessage(Entity<WGPrepBoardComponent> ent, ref WGPrepBoardAssembleMessage args)
    {
        Assemble(ent, args.Actor);
    }

    private void OnTakeMessage(Entity<WGPrepBoardComponent> ent, ref WGPrepBoardTakeMessage args)
    {
        var contents = _containers.GetContainer(ent, WGPrepBoardComponent.Contents);
        if (!TryGetEntity(args.Item, out var item) || !contents.Contains(item.Value))
            return;

        _containers.Remove(item.Value, contents);
        _hands.PickupOrDrop(args.Actor, item.Value);
        UpdateBoardUi(ent);
    }

    /// <summary>Sends the board's window what lies on it and the dishes it is working toward, closest first.</summary>
    private void UpdateBoardUi(Entity<WGPrepBoardComponent> board)
    {
        var items = _containers.GetContainer(board, WGPrepBoardComponent.Contents).ContainedEntities.ToList();
        var views = new List<WGPrepItemView>();
        foreach (var e in items)
        {
            var options = ChopOptions(e);
            var cuts = options.Count > 0 ? Math.Clamp(options[0].Count, 1, 4) : 0;
            var names = options.Select(o => _proto.TryIndex<EntityPrototype>(o.Result, out var p) ? p.Name : o.Result.Id).ToList();
            var optionCuts = options.Select(o => Math.Clamp(o.Count, 1, 4)).ToList();
            views.Add(new WGPrepItemView(GetNetEntity(e), Name(e), MetaData(e).EntityPrototype?.ID, cuts, names, optionCuts));
        }

        var recipes = new List<(int Missing, WGPrepRecipeView View)>();
        foreach (var recipe in PrepRecipes())
        {
            var have = new List<string>();
            var missing = new List<string>();
            var any = false;
            foreach (var ingredient in recipe.Ingredients)
            {
                var count = Math.Min(ingredient.Count, items.Count(e => Fits(e, ingredient)));
                var label = IngredientName(ingredient);
                any |= count > 0;
                if (count > 0)
                    have.Add(Loc.GetString("wg-cook-board-count", ("name", label), ("count", count)));
                if (count < ingredient.Count)
                    missing.Add(Loc.GetString("wg-cook-board-count", ("name", label), ("count", ingredient.Count - count)));
            }

            if (any && _proto.TryIndex<EntityPrototype>(recipe.Result, out var result))
                recipes.Add((missing.Count, new WGPrepRecipeView(result.Name, recipe.Result.Id, have, missing, false)));
        }

        // What lies on the board by prototype, counted once; recipes that use none of it are skipped outright.
        var onBoard = new Dictionary<string, int>();
        foreach (var e in items)
        {
            if (MetaData(e).EntityPrototype?.ID is { } pid)
                onBoard[pid] = onBoard.GetValueOrDefault(pid) + 1;
        }

        foreach (var recipe in BoardRecipes(board))
        {
            if (!recipe.IngredientsSolids.Keys.Any(onBoard.ContainsKey))
                continue;

            var have = new List<string>();
            var missing = new List<string>();
            var any = false;
            foreach (var (id, need) in recipe.IngredientsSolids)
            {
                var count = Math.Min(need.Int(), onBoard.GetValueOrDefault(id));
                var label = _proto.TryIndex<EntityPrototype>(id, out var proto) ? proto.Name : id;
                any |= count > 0;
                if (count > 0)
                    have.Add(Loc.GetString("wg-cook-board-count", ("name", label), ("count", count)));
                if (count < need.Int())
                    missing.Add(Loc.GetString("wg-cook-board-count", ("name", label), ("count", need.Int() - count)));
            }

            foreach (var (reagent, amount) in recipe.IngredientsReagents)
            {
                var label = _proto.TryIndex<ReagentPrototype>(reagent, out var r) ? r.LocalizedName : reagent;
                missing.Add(Loc.GetString("wg-cook-board-reagent", ("name", label), ("amount", amount.Int())));
            }

            if (!any || !_proto.TryIndex<EntityPrototype>(recipe.Result, out var result))
                continue;
            if (Holds(items, recipe))
                missing.Clear();
            var unbaked = (recipe.RecipeType & (int) MicrowaveRecipeType.Assembler) == 0;
            recipes.Add((missing.Count, new WGPrepRecipeView(result.Name, recipe.Result, have, missing, unbaked)));
        }

        var state = new WGPrepBoardState(views, recipes.OrderBy(r => r.Missing).ThenBy(r => r.View.Name).Take(12).Select(r => r.View).ToList());
        _ui.SetUiState(board.Owner, WGPrepBoardUiKey.Key, state);
    }

    private string IngredientName(WGPrepIngredient ingredient)
    {
        if (ingredient.Ids.Count > 0 && _proto.TryIndex<EntityPrototype>(ingredient.Ids[0], out var proto))
            return ingredient.Ids.Count > 1 ? Loc.GetString("wg-cook-board-any", ("name", proto.Name)) : proto.Name;
        return ingredient.Tag?.Id ?? "?";
    }

    private bool Fits(EntityUid item, WGPrepIngredient ingredient)
    {
        var id = MetaData(item).EntityPrototype?.ID;
        return id != null && ingredient.Ids.Any(i => i.Id == id) || ingredient.Tag is { } tag && _tags.HasTag(item, tag);
    }

    private HashSet<string>? _kitchenSolids;

    /// <summary>Every prototype a kitchen recipe takes as a solid.</summary>
    private HashSet<string> KitchenSolids()
    {
        return _kitchenSolids ??= _proto.EnumeratePrototypes<FoodRecipePrototype>().SelectMany(r => r.IngredientsSolids.Keys).ToHashSet();
    }

    /// <summary>What the board's contents would make if put together, for examine: a name, or null.</summary>
    private string? Preview(EntityUid board, BaseContainer contents)
    {
        var items = contents.ContainedEntities.ToList();
        foreach (var recipe in PrepRecipes())
        {
            if (recipe.Ingredients.All(i => items.Count(e => Fits(e, i)) >= i.Count) && _proto.TryIndex<EntityPrototype>(recipe.Result, out var raw))
                return raw.Name;
        }

        var kitchen = BoardRecipes(board).FirstOrDefault(r => Holds(items, r));
        if (kitchen == null || !_proto.TryIndex<EntityPrototype>(kitchen.Result, out var result))
            return null;
        return (kitchen.RecipeType & (int) MicrowaveRecipeType.Assembler) != 0
            ? result.Name
            : Loc.GetString("wg-cook-unbaked-name", ("dish", result.Name));
    }

    /// <summary>The board's "put together": makes the first dish its contents make, if any, and says so.</summary>
    public EntityUid? Assemble(Entity<WGPrepBoardComponent> board, EntityUid user)
    {
        var contents = _containers.GetContainer(board, WGPrepBoardComponent.Contents);
        var made = TryAssemble(board, contents);
        UpdateBoardUi(board);
        _popup.PopupEntity(made is { } dish ? Loc.GetString("wg-cook-board-made", ("item", dish)) : Loc.GetString("wg-cook-board-makes-nothing"),
            board, user, made != null ? PopupType.Medium : PopupType.Small);
        if (made != null)
            _audio.PlayPvs(ChopSound, board, AudioParams.Default.WithVolume(-6f));
        return made;
    }

    private void OnBoardVerbs(Entity<WGPrepBoardComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wg-cook-board-verb"),
            Act = () => Assemble(ent, user),
            Priority = 2,
        });
    }

    /// <summary>Puts the board's contents together into the first dish they make, if any.</summary>
    private EntityUid? TryAssemble(EntityUid board, BaseContainer contents)
    {
        foreach (var recipe in PrepRecipes())
        {
            var free = contents.ContainedEntities.ToList();
            var used = new List<EntityUid>();
            var ok = true;
            foreach (var ingredient in recipe.Ingredients)
            {
                var picks = free.Where(e => Fits(e, ingredient)).Take(ingredient.Count).ToList();
                if (picks.Count < ingredient.Count)
                {
                    ok = false;
                    break;
                }

                used.AddRange(picks);
                free.RemoveAll(picks.Contains);
            }

            if (!ok)
                continue;

            var quality = Average(used);
            foreach (var item in used)
                Del(item);
            var dish = SpawnInContainerOrDrop(recipe.Result, board, WGPrepBoardComponent.Contents);
            SetQuality(dish, quality);
            return dish;
        }

        // Then the kitchen's recipes, as the board offers them: assembler ones (sandwiches, salads) come out ready.
        var items = contents.ContainedEntities.ToList();
        var kitchen = BoardRecipes(board).FirstOrDefault(r => Holds(items, r));
        if (kitchen == null || PickSolids(items, kitchen) is not { } solids)
            return null;

        var dishQuality = Average(solids);
        Consume(items, kitchen, solids);
        if ((kitchen.RecipeType & (int) MicrowaveRecipeType.Assembler) != 0)
        {
            EntityUid? first = null;
            for (var i = 0; i < kitchen.ResultCount; i++)
            {
                var made = SpawnInContainerOrDrop(kitchen.Result, board, WGPrepBoardComponent.Contents);
                SetQuality(made, dishQuality);
                first ??= made;
            }

            return first;
        }

        var unbaked = SpawnInContainerOrDrop(RawDish, board, WGPrepBoardComponent.Contents);
        SetQuality(unbaked, dishQuality);
        EnsureComp<WGRawDishComponent>(unbaked).Recipe = kitchen.ID;
        if (_proto.TryIndex<EntityPrototype>(kitchen.Result, out var result))
            _metaData.SetEntityName(unbaked, Loc.GetString("wg-cook-unbaked-name", ("dish", result.Name)));
        return unbaked;
    }

    private void OnBoardExamined(Entity<WGPrepBoardComponent> ent, ref ExaminedEvent args)
    {
        var container = _containers.GetContainer(ent, WGPrepBoardComponent.Contents);
        var contents = container.ContainedEntities;
        args.PushMarkup(contents.Count == 0
            ? Loc.GetString("wg-cook-board-empty")
            : Loc.GetString("wg-cook-board-holds", ("items", string.Join(", ", contents.Select(e => Name(e))))));
        if (contents.Count > 0 && Preview(ent, container) is { } dish)
            args.PushMarkup(Loc.GetString("wg-cook-board-could-make", ("dish", dish)));
    }

    #endregion
}
