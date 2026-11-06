using Content.Server.Nutrition.Components;
using Content.Server.Popups;
using Content.Shared._WF.Fauna;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Gibbing.Events;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Radiation.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Runs the banked food, mood, growth and splitting of <see cref="FaunaGourmongerComponent"/> mobs.
/// </summary>
public sealed partial class FaunaGourmongerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;
    [Dependency] private FaunaCorpseEaterSystem _eater = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPointLightSystem _light = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaGourmongerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FaunaGourmongerComponent, FaunaAteEvent>(OnAte);
        SubscribeLocalEvent<FaunaGourmongerComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<FaunaGourmongerComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FaunaGourmongerComponent, ExaminedEvent>(OnExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaGourmongerComponent, FaunaCorpseEaterComponent>();
        while (query.MoveNext(out var uid, out var gour, out var eater))
        {
            if (!_mobState.IsAlive(uid))
                continue;

            var state = eater.Eating != null ? FaunaGourmongerState.Eating
                : now < gour.SniffEnd ? FaunaGourmongerState.Sniffing
                : FaunaGourmongerState.Idle;
            if (gour.VisualState != state)
            {
                gour.VisualState = state;
                _appearance.SetData(uid, FaunaGourmongerVisuals.State, state);
            }

            if (now < gour.NextTick)
                continue;

            gour.NextTick = now + gour.TickInterval;
            Tick((uid, gour), eater, now);
        }
    }

    private void Tick(Entity<FaunaGourmongerComponent> ent, FaunaCorpseEaterComponent eater, TimeSpan now)
    {
        var comp = ent.Comp;
        comp.Kcal = MathF.Max(0f, comp.Kcal - comp.FastingTicks / 20f);

        if (comp.Meals > 0 && _random.Prob(MathF.Min(1f, (comp.FastingTicks / 50f + comp.Meals) / 100f)))
        {
            comp.Meals--;
            comp.FastingTicks = 0;
            UpdateGlow(ent);
        }
        else
        {
            comp.FastingTicks++;
        }

        if (!comp.Hangry && comp.Kcal < comp.StarvingKcal)
            SetHangry(ent, true);
        else if (comp.Hangry && comp.Kcal > comp.SatisfiedKcal)
            SetHangry(ent, false);
        else if (!comp.Hangry && comp.Kcal < comp.HungryKcal && _random.Prob(comp.GrumbleChance))
            _popup.PopupEntity(Loc.GetString(_random.Pick(comp.Grumbles), ("user", ent.Owner)), ent);

        if (comp.Hangry && eater.Eating == null && now >= comp.NextSniff)
        {
            comp.NextSniff = now + comp.SniffInterval;
            comp.SniffEnd = now + comp.SniffDuration;
            _popup.PopupEntity(Loc.GetString(comp.SniffPopup, ("user", ent.Owner)), ent);
        }

        UpdateRadiation(ent);
    }

    private void OnMapInit(Entity<FaunaGourmongerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.GrowToSplit += CountAlive();
        ent.Comp.NextTick = _timing.CurTime + ent.Comp.TickInterval;
        SetHangry(ent, ent.Comp.Kcal < ent.Comp.StarvingKcal);
        UpdateGlow(ent);
        UpdateRadiation(ent);
    }

    private void OnAte(Entity<FaunaGourmongerComponent> ent, ref FaunaAteEvent args)
    {
        var comp = ent.Comp;
        int growth;
        if (args.Food)
        {
            growth = comp.FoodGrowth;
            if (TryComp<FoodComponent>(args.Meal, out var food) &&
                _solution.TryGetSolution(args.Meal, food.Solution, out _, out var solution))
                comp.Kcal += solution.Volume.Float() * comp.KcalPerFoodUnit;
        }
        else
        {
            growth = comp.CorpseGrowth;
            if (_thresholds.TryGetThresholdForState(args.Meal, MobState.Dead, out var health))
                comp.Kcal += health.Value.Float() * comp.KcalPerCorpseHealth;
        }

        foreach (var state in new[] { MobState.Critical, MobState.Dead })
        {
            if (_thresholds.TryGetThresholdForState(ent, state, out var threshold))
                _thresholds.SetMobStateThreshold(ent, threshold.Value + growth, state);
        }

        comp.Meals += growth;
        UpdateGlow(ent);
        UpdateRadiation(ent);

        if (comp.Meals > comp.GrowToSplit)
            Split(ent);
    }

    private void OnInteractUsing(Entity<FaunaGourmongerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Hangry || !HasComp<FoodComponent>(args.Used) ||
            !TryComp<FaunaCorpseEaterComponent>(ent, out var eater) || !_mobState.IsAlive(ent))
            return;

        args.Handled = true;
        if (eater.Eating != null)
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.BusyPopup, ("user", ent.Owner)), ent, args.User);
            return;
        }

        if (!_hands.TryDrop(args.User, args.Used, Transform(ent).Coordinates) ||
            !_eater.TryStartEating((ent, eater), args.Used))
            return;

        if (ent.Comp.Heart is { } heart && _random.Prob(ent.Comp.HeartChance))
            Spawn(heart, Transform(ent).Coordinates);
    }

    private void OnMobStateChanged(Entity<FaunaGourmongerComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        DropMeat(ent);
        _helper.QueueGib(ent, GibContentsOption.Skip);
    }

    private void OnExamined(Entity<FaunaGourmongerComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Kcal < ent.Comp.HungryKcal)
            args.PushMarkup(Loc.GetString(ent.Comp.FamishedExamine, ("user", ent.Owner)));
        else if (ent.Comp.Kcal > ent.Comp.FatKcal)
            args.PushMarkup(Loc.GetString(ent.Comp.FatExamine, ("user", ent.Owner)));
    }

    private void SetHangry(Entity<FaunaGourmongerComponent> ent, bool hangry)
    {
        ent.Comp.Hangry = hangry;
        _faction.ClearFactions(ent.Owner, false);
        _faction.AddFaction(ent.Owner, hangry ? ent.Comp.HangryFaction : ent.Comp.CalmFaction);
    }

    /// <summary>
    /// Replaces the mob with offspring that share its banked food, unless too many are alive.
    /// </summary>
    private void Split(Entity<FaunaGourmongerComponent> ent)
    {
        if (CountAlive() >= ent.Comp.MaxAlive || MetaData(ent).EntityPrototype is not { } proto)
            return;

        var coords = Transform(ent).Coordinates;
        for (var i = 0; i < ent.Comp.SplitInto; i++)
        {
            var offspring = Spawn(proto.ID, coords);
            if (TryComp<FaunaGourmongerComponent>(offspring, out var gour))
                gour.Kcal += ent.Comp.Kcal * ent.Comp.OffspringKcalShare;
        }

        _mobState.ChangeMobState(ent, MobState.Dead);
    }

    /// <summary>
    /// Drops meat holding the banked food as nutriment, one piece per <see cref="FaunaGourmongerComponent.NutrimentPerMeat"/>.
    /// </summary>
    private void DropMeat(Entity<FaunaGourmongerComponent> ent)
    {
        var coords = Transform(ent).Coordinates;
        var left = ent.Comp.Kcal / ent.Comp.KcalPerNutriment;
        do
        {
            var meat = Spawn(ent.Comp.Meat, coords);
            var add = MathF.Min(ent.Comp.NutrimentPerMeat, left);
            left -= add;
            if (add <= 0 || !TryComp<FoodComponent>(meat, out var food) ||
                !_solution.TryGetSolution(meat, food.Solution, out var soln, out var solution))
                continue;

            solution.MaxVolume += add;
            _solution.TryAddReagent(soln.Value, ent.Comp.Nutriment, add);
        } while (left > 0);
    }

    private void UpdateGlow(Entity<FaunaGourmongerComponent> ent)
    {
        var fraction = ent.Comp.GrowToSplit > 0 ? (float) ent.Comp.Meals / ent.Comp.GrowToSplit : 0f;
        var lit = fraction >= 0.25f;
        _light.SetEnabled(ent, lit);
        if (!lit)
            return;

        _light.SetColor(ent, ent.Comp.GlowColor);
        _light.SetRadius(ent, 2f + 4f * MathF.Min(1f, fraction));
    }

    private void UpdateRadiation(Entity<FaunaGourmongerComponent> ent)
    {
        if (!TryComp<RadiationSourceComponent>(ent, out var source))
            return;

        source.Intensity = ent.Comp.Kcal * ent.Comp.RadiationPerKcal;
        source.Slope = ent.Comp.RadiationRange > 0 ? source.Intensity / ent.Comp.RadiationRange : source.Slope;
        source.Enabled = source.Intensity > 0;
    }

    private int CountAlive()
    {
        var count = 0;
        var query = EntityQueryEnumerator<FaunaGourmongerComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (_mobState.IsAlive(uid))
                count++;
        }

        return count;
    }
}
