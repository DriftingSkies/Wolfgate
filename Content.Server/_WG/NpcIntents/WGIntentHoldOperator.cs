using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._WG.NpcIntents;

/// <summary>Stands still while the intent system holds someone up; the system aims it.</summary>
public sealed partial class WGIntentHoldOperator : HTNOperator
{
    /// <inheritdoc/>
    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return blackboard.ContainsKey(WGNpcIntentSystem.HoldKey) ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished;
    }
}
