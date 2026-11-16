using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Interaction;
using Robust.Shared.Map;

namespace Content.Server._WF.NpcCrew.HTN;

/// <summary>
/// Throws a grenade round the corner a known hostile is holding: shouts, cooks it for the crewman's skill and throws
/// it to a spot with a clear line to the hostile. See <see cref="WFCrewGrenadeSystem"/>.
/// </summary>
public sealed partial class WFCrewGrenadeOperator : HTNOperator
{
    private const string GrenadeKey = "WFCrewGrenade";
    private const string TargetKey = "WFCrewGrenadeTarget";
    private const string AtKey = "WFCrewGrenadeAt";

    [Dependency] private IEntityManager _entManager = default!;
    private WFCrewGrenadeSystem _grenades = default!;
    private WFCrewWeaponSystem _weapons = default!;
    private RotateToFaceSystem _rotate = default!;
    private SharedTransformSystem _xform = default!;

    /// <summary>Nobody this crewman spares may stand within this of where it lands, nor may he.</summary>
    [DataField]
    public float BlastRadius = 4.5f;

    /// <summary>The farthest a hostile may be for him to throw at it.</summary>
    [DataField]
    public float MaxRange = 9f;

    /// <summary>The farthest he throws.</summary>
    [DataField]
    public float ThrowRange = 8f;

    /// <summary>The farthest from the hostile it may land.</summary>
    [DataField]
    public float LandNear = 3f;

    /// <summary>How long the hostile must have been out of his sight, in seconds.</summary>
    [DataField]
    public float MinUnseen = 3f;

    /// <summary>Seconds before anyone of the same crew on the same ship throws again.</summary>
    [DataField]
    public float CrewCooldown = 20f;

    /// <summary>The least fuse, in seconds, left when it lands: the player's time to react to the shout.</summary>
    [DataField]
    public float MinFuse = 1.5f;

    private WFCrewGrenadeRules Rules => new(BlastRadius, MaxRange, ThrowRange, LandNear, MinUnseen, CrewCooldown, MinFuse);

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _grenades = sysManager.GetEntitySystem<WFCrewGrenadeSystem>();
        _weapons = sysManager.GetEntitySystem<WFCrewWeaponSystem>();
        _rotate = sysManager.GetEntitySystem<RotateToFaceSystem>();
        _xform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<WFCrewWeaponComponent>(owner, out var weapon) || !_weapons.Provoked(owner))
            return (false, null);

        var hunted = _weapons.Focus((owner, weapon), _entManager.GetComponentOrNull<HTNComponent>(owner));
        if (!_grenades.TryPlan(owner, hunted, Rules, out var grenade, out var target, out var at))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { GrenadeKey, grenade },
            { TargetKey, target },
            { AtKey, at },
        });
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>(GrenadeKey, out var grenade, _entManager)
            || !blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager)
            || !blackboard.TryGetValue<EntityCoordinates>(AtKey, out var at, _entManager))
            return;

        _grenades.Ready(owner, grenade, target, at, Rules);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<WFCrewArmedGrenadeComponent>(owner, out var armed))
            return HTNOperatorStatus.Finished;

        _rotate.TryFaceCoordinates(owner, _xform.ToMapCoordinates(armed.At).Position);
        return _grenades.Tick(owner) ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        _grenades.Release(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        blackboard.Remove<EntityUid>(GrenadeKey);
        blackboard.Remove<EntityUid>(TargetKey);
        blackboard.Remove<EntityCoordinates>(AtKey);
    }
}
