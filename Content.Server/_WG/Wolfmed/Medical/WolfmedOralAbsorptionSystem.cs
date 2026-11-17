using Content.Server.Body.Components;
using Content.Shared._WG.Wolfmed.CCVar;
using Content.Shared._WG.Wolfmed.Reagents;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Wolfmed.Medical;

/// <summary>
/// Swallowed painkillers reach the blood in <see cref="WolfmedCVars.PainkillerAbsorbSeconds"/> instead of the
/// stomach's 20 s (playtest 1), so a pill or a swig does something the patient can feel within about ten seconds.
/// The stomach asks through its one marked line; everything else it digests keeps its own delay.
/// </summary>
public sealed partial class WolfmedOralAbsorptionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    /// <summary>Salt and water stay food in a body: the saline they would form is lethal to Avali.</summary>
    private const string SalineReaction = "Saline";

    private readonly Dictionary<string, bool> _painkillers = new();

    /// <summary>Whether a reaction is refused inside a stomach or a bloodstream.</summary>
    public static bool BlockedInBody(ReactionPrototype reaction) => reaction.ID == SalineReaction;

    /// <summary>
    /// Queues whatever the stomach holds with no delta, such as a reaction's product, which would otherwise
    /// never reach the blood and would fill the stomach for good.
    /// </summary>
    public void TrackUntracked(StomachComponent stomach, Solution contents)
    {
        var deltas = stomach.ReagentDeltas;
        foreach (var reagent in contents.Contents)
        {
            var tracked = FixedPoint2.Zero;
            foreach (var delta in deltas)
            {
                if (delta.ReagentQuantity.Reagent == reagent.Reagent)
                    tracked += delta.ReagentQuantity.Quantity;
            }

            if (reagent.Quantity > tracked)
                deltas.Add(new StomachComponent.ReagentDelta(new ReagentQuantity(reagent.Reagent, reagent.Quantity - tracked)));
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ReagentPrototype>())
            _painkillers.Clear();
    }

    /// <summary>How long this reagent waits in this stomach before it moves to the blood.</summary>
    public TimeSpan GetDigestionDelay(StomachComponent stomach, string reagent)
    {
        if (!IsPainkiller(reagent))
            return stomach.DigestionDelay;

        var fast = TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.PainkillerAbsorbSeconds)));
        return fast < stomach.DigestionDelay ? fast : stomach.DigestionDelay;
    }

    /// <summary>A reagent with a Wolfmed pain relief effect in any of its metabolisms.</summary>
    public bool IsPainkiller(string reagent)
    {
        if (_painkillers.TryGetValue(reagent, out var cached))
            return cached;

        var found = false;
        if (_prototypes.TryIndex<ReagentPrototype>(reagent, out var proto) && proto.Metabolisms is { } metabolisms)
        {
            foreach (var entry in metabolisms.Values)
            {
                foreach (var effect in entry.Effects)
                {
                    if (effect is WolfmedPainRelief)
                        found = true;
                }
            }
        }

        _painkillers[reagent] = found;
        return found;
    }
}
