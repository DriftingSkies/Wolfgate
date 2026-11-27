namespace Content.Server._WG.JoinQueue;

/// <summary>Ticks the join queue.</summary>
public sealed partial class JoinQueueSystem : EntitySystem
{
    [Dependency] private JoinQueueManager _queue = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _queue.Update();
    }
}
