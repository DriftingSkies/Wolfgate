using Content.Server.Body.Components;
using Content.Server.EntityEffects.Effects;
using Content.Server.EntityEffects.Effects.StatusEffects;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Drunk;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.WolfChem.Narcotics;

/// <summary>
/// Builds the health analyzer's conditions readout: drug levels and the reagents raising them, status effects a
/// reagent, high, crash or withdrawal is holding, crashes, and dependence, each with what clears it.
/// </summary>
public sealed class WGAnalyzerConditionsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private WGIntoxicationSystem _intoxication = default!;

    private const string JitterKey = "Jitter";

    private ReagentIndex? _index;

    /// <summary>What each reagent holds, what removes it, and each family's antidotes. Built on first use.</summary>
    private sealed class ReagentIndex
    {
        public readonly Dictionary<string, HashSet<string>> Holds = new();
        public readonly Dictionary<string, List<string>> RemovedBy = new();
        public readonly Dictionary<WGDrugFamily, List<string>> Antidotes = new();
        public readonly HashSet<string> Keys = new();
    }

    /// <summary>A reagent in a solution some organ processes, with its clearance rate.</summary>
    private readonly record struct Found(string Reagent, FixedPoint2 Quantity, float Rate, bool Stomach);

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(args =>
        {
            if (args.WasModified<ReagentPrototype>())
                _index = null;
        });
    }

    /// <summary>The readout for a body.</summary>
    public WGAnalyzerConditions Build(EntityUid target)
    {
        var index = _index ??= BuildIndex();
        var result = new WGAnalyzerConditions();
        var found = Contents(target);

        TryComp<WGIntoxicationComponent>(target, out var intox);
        TryComp<WGDependenceComponent>(target, out var dep);

        if (intox != null)
        {
            foreach (var (profile, level) in intox.Levels)
            {
                if (!_proto.TryIndex(profile, out var proto))
                    continue;

                var tier = proto.TierAt(level);
                var causes = new List<WGAnalyzerCause>();
                foreach (var reagent in found)
                {
                    if (!reagent.Stomach && _intoxication.DosesInBlood(reagent.Reagent, profile))
                        causes.Add(Cause(index, reagent));
                }

                result.Drugs.Add(new WGAnalyzerDrug(proto.Family, tier >= 0 ? proto.PhaseOf(tier) : WGDrugPhase.None,
                    causes, index.Antidotes.GetValueOrDefault(proto.Family) ?? new List<string>()));
            }

            foreach (var profile in _intoxication.ActiveCrashes(intox))
            {
                if (_proto.TryIndex(profile, out var proto))
                    result.Crashes.Add(new WGAnalyzerCrash(proto.Family, intox.Crashes[profile]));
            }
        }

        if (TryComp<StatusEffectsComponent>(target, out var status))
        {
            foreach (var (key, state) in status.ActiveEffects)
            {
                var sources = Sources(intox, dep, key);
                if (!index.Keys.Contains(key) && sources.Count == 0)
                    continue;

                var causes = new List<WGAnalyzerCause>();
                foreach (var reagent in found)
                {
                    if (index.Holds.TryGetValue(reagent.Reagent, out var keys) && keys.Contains(key))
                        causes.Add(Cause(index, reagent));
                }

                var left = (float) (state.Cooldown.Item2 - _timing.CurTime).TotalSeconds;
                result.Held.Add(new WGAnalyzerHeld(key, Math.Max(0f, left), causes, sources));
            }
        }

        if (dep != null)
        {
            foreach (var family in dep.Meter.Keys)
            {
                var habit = WGDependenceSystem.Habit(dep, family);
                if (habit.Meter > 0.01f || habit.Tolerance > 0.01f)
                    result.Habits.Add(habit);
            }
        }

        return result;
    }

    /// <summary>The highs, crashes and withdrawals that keep a status effect going.</summary>
    private List<WGAnalyzerSource> Sources(WGIntoxicationComponent? intox, WGDependenceComponent? dep, string key)
    {
        var sources = new List<WGAnalyzerSource>();
        if (intox != null)
        {
            foreach (var (profile, level) in intox.Levels)
            {
                if (!_proto.TryIndex(profile, out var proto))
                    continue;

                var tier = proto.TierAt(level);
                if (tier >= 0 && proto.Tiers[tier].StatusEffects.Exists(effect => effect.Key == key))
                    sources.Add(new WGAnalyzerSource(WGAnalyzerSourceKind.High, proto.Family));
            }

            foreach (var profile in _intoxication.ActiveCrashes(intox))
            {
                if (_proto.TryIndex(profile, out var proto) && proto.Crash != null
                    && proto.Crash.StatusEffects.Exists(effect => effect.Key == key))
                {
                    sources.Add(new WGAnalyzerSource(WGAnalyzerSourceKind.Crash, proto.Family));
                }
            }
        }

        if (dep != null)
        {
            foreach (var (family, stage) in dep.Stage)
            {
                if (WGDependenceSystem.WithdrawalHolds(family, stage, key))
                    sources.Add(new WGAnalyzerSource(WGAnalyzerSourceKind.Withdrawal, family));
            }
        }

        return sources;
    }

    private static WGAnalyzerCause Cause(ReagentIndex index, Found reagent)
    {
        var clear = reagent.Rate > 0f ? reagent.Quantity.Float() / reagent.Rate : -1f;
        return new WGAnalyzerCause(reagent.Reagent, reagent.Quantity, clear, reagent.Stomach,
            index.RemovedBy.GetValueOrDefault(reagent.Reagent) ?? new List<string>());
    }

    /// <summary>
    /// Every reagent in a solution an organ processes, with the rate it clears at: each organ removes the amount of
    /// the last of its groups the reagent belongs to, and organs on the same solution add up.
    /// </summary>
    private List<Found> Contents(EntityUid body)
    {
        var quantities = new Dictionary<(EntityUid, string), (FixedPoint2 Quantity, float Rate, bool Stomach)>();
        foreach (var organ in _body.GetBodyOrganEntityComps<MetabolizerComponent>(body))
        {
            var metabolizer = organ.Comp1;
            var owner = metabolizer.SolutionOnBody ? body : organ.Owner;
            if (metabolizer.MetabolismGroups == null
                || !_solutions.TryGetSolution(owner, metabolizer.SolutionName, out var soln, out var solution))
            {
                continue;
            }

            var stomach = HasComp<StomachComponent>(organ.Owner);
            foreach (var (reagent, quantity) in solution.Contents)
            {
                if (!_proto.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto) || proto.Metabolisms == null)
                    continue;

                var rate = 0f;
                foreach (var group in metabolizer.MetabolismGroups)
                {
                    if (proto.Metabolisms.TryGetValue(group.Id, out var entry))
                        rate = entry.MetabolismRate.Float() * group.MetabolismRateModifier.Float();
                }

                var key = (soln.Value.Owner, proto.ID);
                var (known, knownRate, knownStomach) = quantities.GetValueOrDefault(key, (quantity, 0f, stomach));
                quantities[key] = (known, knownRate + rate, knownStomach);
            }
        }

        var found = new List<Found>();
        foreach (var ((_, reagent), (quantity, rate, stomach)) in quantities)
        {
            found.Add(new Found(reagent, quantity, rate, stomach));
        }

        return found;
    }

    private ReagentIndex BuildIndex()
    {
        var index = new ReagentIndex();
        var groupRemovers = new List<(string Group, string Remover)>();
        foreach (var reagent in _proto.EnumeratePrototypes<ReagentPrototype>())
        {
            if (reagent.Metabolisms == null)
                continue;

            foreach (var entry in reagent.Metabolisms.Values)
            {
                foreach (var effect in entry.Effects)
                {
                    switch (effect)
                    {
                        case GenericStatusEffect { Type: StatusEffectMetabolismType.Add } added:
                            Hold(index, reagent.ID, added.Key);
                            break;
                        case Content.Server.EntityEffects.Effects.Drunk:
                            Hold(index, reagent.ID, SharedDrunkSystem.DrunkKey);
                            break;
                        case Jitter:
                            Hold(index, reagent.ID, JitterKey);
                            break;
                        case AdjustReagent { Reagent: { } removed } adjust when adjust.Amount < FixedPoint2.Zero && removed != reagent.ID:
                            AddOnce(index.RemovedBy, removed, reagent.ID);
                            break;
                        case AdjustReagent { Group: { } group } adjust when adjust.Amount < FixedPoint2.Zero:
                            groupRemovers.Add((group, reagent.ID));
                            break;
                        case WGClearIntoxication clear:
                            AddOnce(index.Antidotes, clear.Family, reagent.ID);
                            break;
                    }
                }
            }
        }

        if (groupRemovers.Count > 0)
        {
            foreach (var reagent in _proto.EnumeratePrototypes<ReagentPrototype>())
            {
                if (reagent.Metabolisms == null)
                    continue;

                foreach (var (group, remover) in groupRemovers)
                {
                    if (remover != reagent.ID && reagent.Metabolisms.ContainsKey(group))
                        AddOnce(index.RemovedBy, reagent.ID, remover);
                }
            }
        }

        return index;
    }

    private static void Hold(ReagentIndex index, string reagent, string key)
    {
        if (!index.Holds.TryGetValue(reagent, out var keys))
            index.Holds[reagent] = keys = new HashSet<string>();

        keys.Add(key);
        index.Keys.Add(key);
    }

    private static void AddOnce<TKey>(Dictionary<TKey, List<string>> map, TKey key, string value) where TKey : notnull
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = new List<string>();

        if (!list.Contains(value))
            list.Add(value);
    }
}
