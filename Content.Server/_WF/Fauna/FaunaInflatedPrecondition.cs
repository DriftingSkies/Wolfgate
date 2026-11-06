using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Met while the mob's <see cref="FaunaInflateComponent"/> inflation matches <see cref="Inflated"/>.
/// </summary>
public sealed partial class FaunaInflatedPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    /// <summary>
    /// Inflation state the mob must be in for the precondition to pass.
    /// </summary>
    [DataField]
    public bool Inflated = true;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _entManager.TryGetComponent<FaunaInflateComponent>(owner, out var inflate)
               && inflate.Inflated == Inflated;
    }
}
