using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Sends a crewman, weapon drawn, to his place in his crew's hold. See <see cref="WGCrewMusterSystem"/>.</summary>
public sealed partial class WGCrewMusterOperator : HTNOperator
{
    /// <summary>The place the crewman moves to, and how close he gets to it.</summary>
    public const string PlaceKey = "WGCrewMuster";
    public const string RangeKey = "WGCrewMusterRange";

    private const float Range = 0.5f;

    private WGCrewMusterSystem _muster = default!;
    private WGCrewWeaponSystem _weapons = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _muster = sysManager.GetEntitySystem<WGCrewMusterSystem>();
        _weapons = sysManager.GetEntitySystem<WGCrewWeaponSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_muster.TryGetPlace(owner, out var place))
            return (false, null);

        return (true, new Dictionary<string, object> { { PlaceKey, place }, { RangeKey, Range } });
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _weapons.TryDraw(owner);
        _weapons.TryReloadOrSwitch(owner);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return HTNOperatorStatus.Finished;
    }
}
