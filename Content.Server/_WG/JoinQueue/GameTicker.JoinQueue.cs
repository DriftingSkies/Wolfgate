using Content.Server._WG.JoinQueue;

namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    [Dependency] private JoinQueueManager _joinQueue = default!;
}
