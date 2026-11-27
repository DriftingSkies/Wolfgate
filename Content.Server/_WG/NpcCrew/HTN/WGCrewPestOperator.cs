using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>Plans a pest hunt: the pest <see cref="WGCrewPestSystem"/> picked and where it is now.</summary>
public sealed partial class WGCrewPestOperator : HTNOperator
{
    private WGCrewPestSystem _pests = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pests = sysManager.GetEntitySystem<WGCrewPestSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!_pests.TryGetPest(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner), out var pest, out var coordinates))
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));
        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, new Dictionary<string, object>
        {
            { WGCrewPestSystem.PestKey, pest },
            { "WGCrewPestCoordinates", coordinates },
        }));
    }
}
