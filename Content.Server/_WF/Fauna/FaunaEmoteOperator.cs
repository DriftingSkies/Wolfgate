using Content.Server.Chat.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Chat;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Sends a localized emote from the mob, like a source mob's visible message.
/// </summary>
public sealed partial class FaunaEmoteOperator : HTNOperator
{
    private ChatSystem _chat = default!;

    /// <summary>
    /// Fluent string sent as the emote.
    /// </summary>
    [DataField(required: true)]
    public LocId Emote;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _chat = sysManager.GetEntitySystem<ChatSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        _chat.TrySendInGameICMessage(owner, Loc.GetString(Emote), InGameICChatType.Emote, true, hideLog: true);
        return base.Update(blackboard, frameTime);
    }
}
