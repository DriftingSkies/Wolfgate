using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Met while the mob has less than <see cref="Fraction"/> of its health left.
/// </summary>
public sealed partial class FaunaHealthBelowPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    private MobThresholdSystem _thresholds = default!;

    /// <summary>
    /// Health fraction from 0 to 1 that the mob must be below.
    /// </summary>
    [DataField(required: true)]
    public float Fraction;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _thresholds = sysManager.GetEntitySystem<MobThresholdSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<DamageableComponent>(owner, out var damageable)
            || !_thresholds.TryGetDeadThreshold(owner, out var dead)
            || dead <= 0)
        {
            return false;
        }

        return 1f - (damageable.TotalDamage / dead.Value).Float() < Fraction;
    }
}
