using Content.Server.Chat.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared.Chat;
using Robust.Shared.Random;

namespace Content.Server._WG.Tributes;

/// <summary>
/// Now and then, the pilot who starts an FTL jump says "Engage." In memory of Sir Patrick Stewart.
/// </summary>
public sealed partial class WGEngageSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>Chance per jump that the pilot says the line.</summary>
    public const float EngageChance = 1f / 250f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShuttleConsoleFTLTravelStartEvent>(OnFtlStart);
    }

    private void OnFtlStart(ref ShuttleConsoleFTLTravelStartEvent ev)
    {
        if (!_random.Prob(EngageChance)
            || !TryComp<ShuttleConsoleComponent>(ev.Uid, out var console)
            || console.SubscribedPilots.Count == 0)
            return;

        _chat.TrySendInGameICMessage(console.SubscribedPilots[0], Loc.GetString("wg-tribute-engage"),
            InGameICChatType.Speak, hideChat: false);
    }
}
