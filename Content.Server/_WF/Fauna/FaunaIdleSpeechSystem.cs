using Content.Server.Chat.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.Chat;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Makes <see cref="FaunaIdleSpeechComponent"/> mobs say their lines.
/// </summary>
public sealed partial class FaunaIdleSpeechSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaIdleSpeechComponent>();
        while (query.MoveNext(out var uid, out var speech))
        {
            if (now < speech.NextCheck)
                continue;

            speech.NextCheck = now + speech.Interval;
            if (speech.Lines.Count == 0 || !_mobState.IsAlive(uid) || !_random.Prob(speech.Chance))
                continue;

            if (speech.IdleOnly &&
                TryComp<HTNComponent>(uid, out var htn) &&
                htn.Blackboard.ContainsKey(FaunaHelperSystem.TargetKey))
                continue;

            _chat.TrySendInGameICMessage(uid, Loc.GetString(_random.Pick(speech.Lines)), InGameICChatType.Speak,
                hideChat: true, checkRadioPrefix: false);
        }
    }
}
