using System.Linq;
using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._WG.Farming;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Farming;

/// <summary>A processing machine: keg, preserves jar, cheese press, mill, composter, cask, bee hive, compute rack and the like.</summary>
[RegisterComponent, Access(typeof(WGProcessorSystem))]
public sealed partial class WGProcessorComponent : Component
{
    /// <summary>The machine kind; recipes name it.</summary>
    [DataField(required: true)]
    public string Machine = string.Empty;

    [DataField]
    public string? Recipe;

    /// <summary>Seconds of work done and needed for the current batch.</summary>
    [DataField]
    public float Progress;

    [DataField]
    public float Needed;

    [DataField]
    public WGCropQuality Quality;

    /// <summary>The name of what was put in, for examine.</summary>
    [DataField]
    public string? Holding;

    /// <summary>Why it is idle, a loc id; null while it works.</summary>
    [ViewVariables]
    public string? Idle;

    /// <summary>Supplies loaded by hand, by prototype, used up a batch at a time.</summary>
    [DataField]
    public Dictionary<string, int> Stock = new();

    /// <summary>The most of one supply it holds.</summary>
    [DataField]
    public int StockCap = 10;

    /// <summary>The recipe being loaded a batch at a time, and how many items are in so far.</summary>
    [DataField]
    public string? Loading;

    [DataField]
    public int Loaded;

    /// <summary>The seed of the produce put in, for a seed maker.</summary>
    [ViewVariables]
    public SeedData? Seed;
}

/// <summary>
/// Runs processing machines: an item used on one starts a batch of the first recipe that takes it; work goes on while
/// the machine is anchored and powered (or needs no power); the goods pop out with the input's quality. Supplies a recipe needs (bottles)
/// are loaded by hand first.
/// </summary>
public sealed class WGProcessorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private BotanySystem _botany = default!;

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGProcessorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WGProcessorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnInteractUsing(Entity<WGProcessorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (TryStock(ent, args.Used, args.User))
        {
            args.Handled = true;
            return;
        }

        var recipe = Match(ent.Comp.Machine, args.Used);
        if (recipe == null)
            return;

        args.Handled = true;
        if (ent.Comp.Recipe != null)
        {
            _popup.PopupEntity(Loc.GetString("wg-process-busy"), ent, args.User);
            return;
        }

        if (recipe.Needs is { } needs && ent.Comp.Stock.GetValueOrDefault(needs.Id) <= 0)
        {
            _popup.PopupEntity(Loc.GetString("wg-process-needs", ("item", NameOf(needs))), ent, args.User);
            return;
        }

        // Plain produce names its seed by prototype; only mutated produce carries the seed itself.
        SeedData? seed = null;
        if (recipe.Seeds && (!TryComp<ProduceComponent>(args.Used, out var produce) || !_botany.TryGetSeed(produce, out seed)
                || seed.Seedless || seed.PermanentlySeedless))
        {
            _popup.PopupEntity(Loc.GetString("wg-process-no-seeds"), ent, args.User);
            return;
        }

        if (recipe.Batch > 1)
        {
            Load(ent, recipe, args.Used, args.User);
            return;
        }

        if (recipe.Needs is { } used)
            ent.Comp.Stock[used.Id]--;
        ent.Comp.Seed = seed;
        ent.Comp.Quality = CompOrNull<WGProduceQualityComponent>(args.Used)?.Quality ?? WGCropQuality.Normal;
        ent.Comp.Holding = Name(args.Used);
        if (TryComp<StackComponent>(args.Used, out var stack) && stack.Count > 1)
            _stack.SetCount(args.Used, stack.Count - 1, stack);
        else
            QueueDel(args.Used);

        Start(ent, recipe);
        _popup.PopupEntity(Loc.GetString("wg-process-started", ("item", ent.Comp.Holding)), ent, args.User);
    }

    /// <summary>Adds items toward a batch recipe; the batch starts once it holds enough.</summary>
    private void Load(Entity<WGProcessorComponent> ent, WGProcessRecipePrototype recipe, EntityUid item, EntityUid user)
    {
        if (ent.Comp.Loading != null && ent.Comp.Loading != recipe.ID)
        {
            _popup.PopupEntity(Loc.GetString("wg-process-loading-other"), ent, user);
            return;
        }

        ent.Comp.Loading = recipe.ID;
        var want = recipe.Batch - ent.Comp.Loaded;
        var count = Math.Min(want, TryComp<StackComponent>(item, out var stack) ? stack.Count : 1);
        if (stack != null && stack.Count > count)
            _stack.SetCount(item, stack.Count - count, stack);
        else
            QueueDel(item);

        ent.Comp.Loaded += count;
        if (ent.Comp.Loaded < recipe.Batch)
        {
            _popup.PopupEntity(Loc.GetString("wg-process-loaded", ("count", ent.Comp.Loaded), ("batch", recipe.Batch)), ent, user);
            return;
        }

        // A full batch uses its supply too, as a single item does.
        if (recipe.Needs is { } needs)
            ent.Comp.Stock[needs.Id] = Math.Max(0, ent.Comp.Stock.GetValueOrDefault(needs.Id) - 1);
        ent.Comp.Loading = null;
        ent.Comp.Loaded = 0;
        ent.Comp.Quality = WGCropQuality.Normal;
        ent.Comp.Holding = null;
        Start(ent, recipe);
        _popup.PopupEntity(Loc.GetString("wg-process-batch-started"), ent, user);
    }

    /// <summary>Loads a supply some recipe of the machine needs, such as a bottle into a keg.</summary>
    private bool TryStock(Entity<WGProcessorComponent> ent, EntityUid item, EntityUid user)
    {
        var id = MetaData(item).EntityPrototype?.ID;
        if (id == null || !_proto.EnumeratePrototypes<WGProcessRecipePrototype>().Any(r => r.Machine == ent.Comp.Machine && r.Needs?.Id == id))
            return false;

        var have = ent.Comp.Stock.GetValueOrDefault(id);
        var room = ent.Comp.StockCap - have;
        if (room <= 0)
        {
            _popup.PopupEntity(Loc.GetString("wg-process-stock-full"), ent, user);
            return true;
        }

        var count = Math.Min(room, TryComp<StackComponent>(item, out var stack) ? stack.Count : 1);
        if (stack != null && stack.Count > count)
            _stack.SetCount(item, stack.Count - count, stack);
        else
            QueueDel(item);
        ent.Comp.Stock[id] = have + count;
        _popup.PopupEntity(Loc.GetString("wg-process-stocked", ("count", have + count), ("item", NameOf(id))), ent, user);
        return true;
    }

    private string NameOf(string id)
    {
        return _proto.TryIndex<EntityPrototype>(id, out var proto) ? proto.Name : id;
    }

    /// <summary>Why the machine can't run now, as a loc id, or null.</summary>
    private string? Gate(EntityUid uid)
    {
        var xform = Transform(uid);
        if (!xform.Anchored || xform.GridUid == null)
            return "wg-process-idle-anchor";
        if (!_power.IsPowered(uid))
            return "wg-process-idle-power";
        return null;
    }

    private WGProcessRecipePrototype? Match(string machine, EntityUid item)
    {
        var id = MetaData(item).EntityPrototype?.ID;
        foreach (var recipe in _proto.EnumeratePrototypes<WGProcessRecipePrototype>().OrderBy(r => r.ID))
        {
            if (recipe.Machine != machine || recipe.Auto)
                continue;
            if (id != null && recipe.Inputs.Any(i => i.Id == id)
                || recipe.Tags.Count > 0 && _tags.HasAnyTag(item, recipe.Tags)
                || recipe.Produce && HasComp<ProduceComponent>(item))
                return recipe;
        }

        return null;
    }

    private void Start(Entity<WGProcessorComponent> ent, WGProcessRecipePrototype recipe)
    {
        ent.Comp.Recipe = recipe.ID;
        ent.Comp.Progress = 0;
        ent.Comp.Needed = recipe.Minutes * 60f;
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;
        _next = _timing.CurTime + Tick;

        var query = EntityQueryEnumerator<WGProcessorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ent = (uid, comp);
            if (comp.Recipe == null)
            {
                if (AutoRecipe(comp.Machine) is not { } auto)
                    continue;
                comp.Quality = WGCropQuality.Normal;
                comp.Holding = null;
                Start(ent, auto);
            }

            if (!_proto.TryIndex<WGProcessRecipePrototype>(comp.Recipe!, out var recipe))
            {
                comp.Recipe = null;
                continue;
            }

            comp.Idle = Gate(uid);
            if (comp.Idle == null && recipe.NeedsPlants > 0 && Planted(uid) < recipe.NeedsPlants)
                comp.Idle = "wg-process-idle-plants";
            if (comp.Idle != null)
                continue;

            comp.Progress += (float) Tick.TotalSeconds;
            if (comp.Progress < comp.Needed)
                continue;

            Finish(ent, recipe);
        }
    }

    private WGProcessRecipePrototype? AutoRecipe(string machine)
    {
        return _proto.EnumeratePrototypes<WGProcessRecipePrototype>().Where(r => r.Auto && r.Machine == machine).MinBy(r => r.ID);
    }

    private int Planted(EntityUid uid)
    {
        var count = 0;
        foreach (var holder in _lookup.GetEntitiesInRange<PlantHolderComponent>(Transform(uid).Coordinates, 5f))
        {
            if (holder.Comp.Seed != null && !holder.Comp.Dead)
                count++;
        }

        return count;
    }

    private void Finish(Entity<WGProcessorComponent> ent, WGProcessRecipePrototype recipe)
    {
        var quality = recipe.KeepQuality ? ent.Comp.Quality : WGCropQuality.Normal;
        if (recipe.QualityUp && quality < WGCropQuality.Iridium)
            quality++;

        var pick = Pick(recipe.Outputs);
        for (var i = 0; i < pick.Count; i++)
        {
            if (recipe.Seeds)
            {
                // A mutated seed is unique, and packets of it would share it, so one packet's mutations would change the
                // others; only botany may copy a seed, so a mutated seed makes one packet.
                if (ent.Comp.Seed is { } seed && (i == 0 || !seed.Unique))
                    _botany.SpawnSeedPacket(seed, Transform(ent).Coordinates, ent);
                continue;
            }

            var made = Spawn(pick.Id, Transform(ent).Coordinates);
            if (quality != WGCropQuality.Normal)
                EnsureComp<WGProduceQualityComponent>(made).Quality = quality;
        }

        _popup.PopupEntity(Loc.GetString("wg-process-done", ("machine", ent.Owner)), ent);
        ent.Comp.Recipe = null;
        ent.Comp.Progress = 0;
        ent.Comp.Holding = null;
        ent.Comp.Seed = null;
    }

    private WGProcessOutput Pick(List<WGProcessOutput> outputs)
    {
        var total = outputs.Sum(o => o.Weight);
        var roll = _random.NextFloat() * total;
        foreach (var output in outputs)
        {
            roll -= output.Weight;
            if (roll <= 0)
                return output;
        }

        return outputs[^1];
    }

    private void OnExamined(Entity<WGProcessorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Loading != null && _proto.TryIndex<WGProcessRecipePrototype>(ent.Comp.Loading, out var loading))
            args.PushMarkup(Loc.GetString("wg-process-loaded", ("count", ent.Comp.Loaded), ("batch", loading.Batch)));

        foreach (var (id, count) in ent.Comp.Stock)
        {
            if (count > 0)
                args.PushMarkup(Loc.GetString("wg-process-stock", ("count", count), ("item", NameOf(id))));
        }

        if (ent.Comp.Recipe == null)
        {
            args.PushMarkup(Loc.GetString("wg-process-empty"));
            return;
        }

        var percent = ent.Comp.Needed <= 0 ? 100 : (int) (100 * ent.Comp.Progress / ent.Comp.Needed);
        args.PushMarkup(ent.Comp.Holding is { } holding
            ? Loc.GetString("wg-process-working", ("item", holding), ("percent", percent))
            : Loc.GetString("wg-process-working-auto", ("percent", percent)));
        if (ent.Comp.Idle is { } idle)
            args.PushMarkup(Loc.GetString(idle));
    }
}
