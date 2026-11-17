using Content.Server.Chat.Managers;
using Content.Shared.Mind.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.Chimera;

/// <summary>
/// Greets the player who takes a mob with <see cref="MindGreetingComponent"/>.
/// </summary>
public sealed partial class MindGreetingSystem : EntitySystem
{
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MindGreetingComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnMindAdded(Entity<MindGreetingComponent> ent, ref MindAddedMessage args)
    {
        if (ent.Comp.Greeted || !_player.TryGetSessionById(args.Mind.Comp.UserId, out var session))
            return;

        ent.Comp.Greeted = true;
        _chat.DispatchServerMessage(session, Loc.GetString(ent.Comp.Message));
    }
}
