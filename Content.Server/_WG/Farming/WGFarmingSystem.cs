using Content.Server._WG.Planets;
using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Power.Components;
using Content.Shared._WG.Farming;
using Content.Shared.Cargo.Components;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Farming;

/// <summary>Botany's growth, reworked after Stardew and RimWorld, for every plant holder.</summary>
public sealed class WGFarmingSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedRoofSystem _roofs = default!;
    [Dependency] private WGPlanetClimateSystem _climate = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private PlantHolderSystem _plants = default!;

    private const string Water = "Water";

    /// <summary>Light level above which open ground counts as day.</summary>
    private const double DayLight = 0.35;

    /// <summary>Upstream ages a plant on 80% of its cycles.</summary>
    private const float AgeChance = 0.8f;

    /// <summary>Chance a bird pecks an unguarded outdoor crop each check, about once in three hours of growing.</summary>
    private const float PeckChance = 1f / 2000f;

    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(5);

    /// <summary>How far round a plant to look for scarecrows and grow lamps; each then has its own range.</summary>
    private const float ScarecrowReach = 16f;
    private const float LampReach = 8f;
    private static readonly float[] QualityPrice = { 1f, 1.25f, 1.5f, 2f };

    private TimeSpan _next;

    /// <summary>Seed names to seed IDs; a planted seed is a copy that keeps its name, not its prototype ID.</summary>
    private readonly Dictionary<string, string> _seedIds = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPlantHarvestedEvent>(OnHarvested);
        SubscribeLocalEvent<WGFarmPlotComponent, ExaminedEvent>(OnPlotExamined);
        SubscribeLocalEvent<WGProduceQualityComponent, ExaminedEvent>(OnProduceExamined);
        SubscribeLocalEvent<WGProduceQualityComponent, PriceCalculationEvent>(OnPrice);
        SubscribeLocalEvent<WGFertilizerComponent, AfterInteractEvent>(OnFertilize);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => IndexSeeds());
        IndexSeeds();
    }

    private void IndexSeeds()
    {
        _seedIds.Clear();
        foreach (var seed in _proto.EnumeratePrototypes<SeedPrototype>())
        {
            _seedIds.TryAdd(seed.Name, seed.ID);
        }
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;
        _next = _timing.CurTime + CheckEvery;

        Sprinkle();
        var query = EntityQueryEnumerator<PlantHolderComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var holder, out var xform))
        {
            if (xform.GridUid is not { } grid)
                continue;

            var plot = EnsureComp<WGFarmPlotComponent>(uid);
            if (holder.Seed == null || holder.Dead)
            {
                plot.LitChecks = plot.CaredChecks = 0;
                plot.Reason = null;
                // Fertilizer stays in the soil until the plant it fed is gone; soil fertilized before planting keeps it.
                if (holder.Seed == null && plot.HadSeed)
                {
                    plot.FertQuality = 0;
                    plot.FertSpeed = 0;
                    plot.Retain = false;
                }

                plot.HadSeed = holder.Seed != null;
                continue;
            }

            plot.HadSeed = true;

            Tend(uid, holder, plot, grid, xform);
        }
    }

    private void Tend(EntityUid uid, PlantHolderComponent holder, WGFarmPlotComponent plot, EntityUid grid, TransformComponent xform)
    {
        var crop = _seedIds.TryGetValue(holder.Seed!.Name, out var seedId) && _proto.TryIndex<WGCropPrototype>(seedId, out var c) ? c : null;
        var map = xform.MapUid;
        var day = map != null && TryComp<LightCycleComponent>(map, out var cycle) && cycle.Enabled ? cycle : null;

        // A crop's minutes become a cycle length, so Maturation cycles pass in that many minutes of growing.
        if (crop != null && holder.Seed.Maturation > 0)
        {
            var seconds = crop.Minutes * 60f * AgeChance / holder.Seed.Maturation;
            holder.CycleDelay = TimeSpan.FromSeconds(Math.Max(5f, seconds / (1f + plot.FertSpeed)));
        }

        // Retaining soil waters a dry plant once a planet day.
        if (plot.Retain && holder.WaterLevel <= 10f && day != null)
        {
            var today = (int) ((_timing.CurTime + day.Offset).TotalSeconds / Math.Max(1, day.Duration.TotalSeconds));
            if (today != plot.RetainDay)
            {
                plot.RetainDay = today;
                holder.WaterLevel = 40f;
            }
        }

        var indoors = TryComp<MapGridComponent>(grid, out var gridComp) && TryComp<RoofComponent>(grid, out var roof)
            && _roofs.IsRooved((grid, gridComp, roof), _maps.TileIndicesFor(grid, gridComp, xform.Coordinates));
        var lamp = UnderLamp(uid);
        // Without a day (stations, ships) the room's own lights grow plants; on a world, the sun or a lamp.
        var daylight = day == null || !indoors
            && SharedLightCycleSystem.CalculateLightLevel(day, (float) (_timing.CurTime + day.Offset).TotalSeconds) >= DayLight;

        string? reason = null;
        if (!daylight && !lamp)
            reason = indoors ? "wg-farm-reason-dark-indoors" : "wg-farm-reason-night";
        else if (crop != null && day != null && !(indoors && lamp) && map != null && _climate.GetOutsideTemperature(map.Value) is { } kelvin)
        {
            var celsius = kelvin - 273.15f;
            if (celsius < crop.MinTemp)
                reason = "wg-farm-reason-cold";
            else if (celsius > crop.MaxTemp)
                reason = "wg-farm-reason-hot";
        }

        var lit = reason == null;
        if (lit && holder.WaterLevel <= 10f)
            reason = "wg-farm-reason-dry";

        if (lit)
        {
            plot.LitChecks++;
            if (reason == null && holder.NutritionLevel > 20f)
                plot.CaredChecks++;
        }

        plot.Reason = reason;
        if (reason == null && day != null && !indoors && Pecked(uid, holder))
            reason = plot.Reason = "wg-farm-reason-pecked";
        if (reason != null)
        {
            holder.SkipAging = Math.Max(holder.SkipAging, 1);
            plot.Stalled = true;
        }
        else if (plot.Stalled)
        {
            // The stall only ever raised SkipAging to 1, which botany counts down in a cycle; zeroing it would also
            // wipe skips botany added itself (cryoxadone, harvests, forced updates).
            plot.Stalled = false;
        }
    }

    /// <summary>Rolls a bird peck on an outdoor crop unless a scarecrow covers it.</summary>
    private bool Pecked(EntityUid uid, PlantHolderComponent holder)
    {
        if (!_random.Prob(PeckChance))
            return false;

        var at = Transform(uid).Coordinates;
        foreach (var crow in _lookup.GetEntitiesInRange<WGScarecrowComponent>(at, ScarecrowReach))
        {
            if (_transform.InRange(Transform(crow).Coordinates, at, crow.Comp.Range))
                return false;
        }

        holder.Health -= 20f;
        _popup.PopupEntity(Loc.GetString("wg-farm-pecked"), uid);
        return true;
    }

    private bool UnderLamp(EntityUid uid)
    {
        var at = Transform(uid).Coordinates;
        foreach (var lamp in _lookup.GetEntitiesInRange<WGGrowLampComponent>(at, LampReach))
        {
            if (TryComp<ApcPowerReceiverComponent>(lamp, out var power) && !power.Powered)
                continue;
            if (_transform.InRange(Transform(lamp).Coordinates, at, lamp.Comp.Range))
                return true;
        }

        return false;
    }

    private void OnHarvested(ref WGPlantHarvestedEvent args)
    {
        if (!TryComp<WGFarmPlotComponent>(args.Holder, out var plot))
            return;

        var care = plot.LitChecks == 0 ? 0f : (float) plot.CaredChecks / plot.LitChecks;
        foreach (var produce in args.Produce)
        {
            var quality = Roll(care, plot.FertQuality);
            if (quality == WGCropQuality.Normal)
                continue;
            EnsureComp<WGProduceQualityComponent>(produce).Quality = quality;
        }

        // Regrowing crops start counting afresh.
        plot.LitChecks = plot.CaredChecks = 0;
    }

    /// <summary>Rolls a quality: full care gives about a third silver and a sixth gold.</summary>
    private WGCropQuality Roll(float care, int fertilizer)
    {
        var roll = _random.NextFloat();
        var quality = roll < care * 0.15f ? WGCropQuality.Gold
            : roll < care * 0.45f ? WGCropQuality.Silver
            : WGCropQuality.Normal;
        var top = fertilizer >= 2 ? WGCropQuality.Iridium : WGCropQuality.Gold;
        for (var i = 0; i < fertilizer; i++)
        {
            if (quality < top && _random.Prob(0.5f))
                quality++;
        }

        return quality;
    }

    /// <summary>Waters the plant holders round each working sprinkler.</summary>
    private void Sprinkle()
    {
        var query = EntityQueryEnumerator<WGSprinklerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var sprinkler, out var xform))
        {
            if (!xform.Anchored || TryComp<ApcPowerReceiverComponent>(uid, out var power) && !power.Powered)
                continue;

            // It draws from its own tank: a unit of water for each plant it tops up.
            if (!_solutions.TryGetSolution(uid, sprinkler.Solution, out var tank, out var water))
                continue;

            // A square's corners lie further out than its range, so look as far as them.
            var at = xform.LocalPosition;
            var reach = sprinkler.Range * (sprinkler.Square ? 1.42f : 1f) + 0.5f;
            foreach (var holder in _lookup.GetEntitiesInRange<PlantHolderComponent>(xform.Coordinates, reach))
            {
                // In the sprinkler's own frame, so the plus and the square follow the grid's tiles.
                var offset = _transform.WithEntityId(Transform(holder).Coordinates, xform.ParentUid).Position - at;
                var dx = MathF.Round(MathF.Abs(offset.X));
                var dy = MathF.Round(MathF.Abs(offset.Y));
                var inside = sprinkler.Square ? Math.Max(dx, dy) <= sprinkler.Range : dx + dy <= sprinkler.Range;
                if (!inside || holder.Comp.WaterLevel >= 60f)
                    continue;
                if (water.GetTotalPrototypeQuantity(Water) < FixedPoint2.New(sprinkler.UsePerWatering)
                    || !_solutions.RemoveReagent(tank.Value, Water, FixedPoint2.New(sprinkler.UsePerWatering)))
                    break;
                holder.Comp.WaterLevel = 60f;
                _plants.UpdateSprite(holder, holder.Comp);
            }
        }
    }

    private void OnFertilize(Entity<WGFertilizerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<PlantHolderComponent>(target))
            return;

        args.Handled = true;
        var plot = EnsureComp<WGFarmPlotComponent>(target);
        if (plot.FertQuality >= ent.Comp.Quality && plot.FertSpeed >= ent.Comp.Speed && (plot.Retain || !ent.Comp.Retain))
        {
            _popup.PopupEntity(Loc.GetString("wg-farm-fertilizer-already"), target, args.User);
            return;
        }

        plot.FertQuality = Math.Max(plot.FertQuality, ent.Comp.Quality);
        plot.FertSpeed = Math.Max(plot.FertSpeed, ent.Comp.Speed);
        plot.Retain |= ent.Comp.Retain;
        _popup.PopupEntity(Loc.GetString("wg-farm-fertilized", ("item", ent.Owner)), target, args.User);
        if (TryComp<StackComponent>(ent, out var stack) && stack.Count > 1)
            _stack.SetCount(ent, stack.Count - 1, stack);
        else
            QueueDel(ent);
    }

    private void OnPlotExamined(Entity<WGFarmPlotComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || !TryComp<PlantHolderComponent>(ent, out var holder) || holder.Seed == null || holder.Dead)
            return;
        args.PushMarkup(Loc.GetString(ent.Comp.Reason ?? "wg-farm-reason-growing"));
        if (ent.Comp.FertQuality > 0 || ent.Comp.FertSpeed > 0 || ent.Comp.Retain)
            args.PushMarkup(Loc.GetString("wg-farm-fertilizer-examine", ("quality", ent.Comp.FertQuality), ("speed", (int) (ent.Comp.FertSpeed * 100)), ("retain", ent.Comp.Retain ? 1 : 0)));
    }

    private void OnProduceExamined(Entity<WGProduceQualityComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wg-farm-quality", ("quality", ent.Comp.Quality.ToString().ToLowerInvariant())));
    }

    private void OnPrice(Entity<WGProduceQualityComponent> ent, ref PriceCalculationEvent args)
    {
        if (TryComp<StaticPriceComponent>(ent, out var price))
            args.Price += price.Price * (QualityPrice[(int) ent.Comp.Quality] - 1f);
    }
}

/// <summary>Raised broadcast when a plant holder is harvested by hand, with the produce it gave.</summary>
[ByRefEvent]
public readonly record struct WGPlantHarvestedEvent(EntityUid Holder, List<EntityUid> Produce);
