using Content.Shared._WF.Xenonids.Parasite;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;

namespace Content.Server._WF.Xenonids.Parasite;

/// <summary>When the player who burst takes their larva, their old body becomes unrepairable.</summary>
public sealed class WFBurstHostSystem : EntitySystem
{
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedXenoParasiteSystem _parasite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFBurstLarvaComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnMindAdded(Entity<WFBurstLarvaComponent> ent, ref MindAddedMessage args)
    {
        if (ent.Comp.Host is not { } host || TerminatingOrDeleted(host))
            return;

        if (!_mind.TryGetMind(ent, out var mindId, out var mind))
            return;

        if (_parasite.IsBurstHostPlayer(host, mindId, mind.UserId))
            _parasite.MarkBurstHostLeft(host);
    }
}
