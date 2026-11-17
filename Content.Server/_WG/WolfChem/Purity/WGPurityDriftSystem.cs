using Content.Server.GameTicking.Events;
using Content.Shared._WG.WolfChem.Purity;
using Content.Shared.Chemistry.Reaction;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WG.WolfChem.Purity;

/// <summary>
/// Moves each graded reaction's optimal temperature by up to its <see cref="WGReactionPurityPrototype.Drift"/> at
/// round start, so last round's numbers are only close.
/// </summary>
public sealed class WGPurityDriftSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChemicalReactionSystem _reactions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartingEvent>(_ => Roll());
    }

    /// <summary>Rolls a new shift for every reaction that drifts.</summary>
    public void Roll()
    {
        _reactions.WGOptimumShift.Clear();
        foreach (var purity in _proto.EnumeratePrototypes<WGReactionPurityPrototype>())
        {
            if (purity.OptimalTemp != null && purity.Drift > 0f)
                _reactions.WGOptimumShift[purity.ID] = _random.NextFloat(-purity.Drift, purity.Drift);
        }
    }
}
