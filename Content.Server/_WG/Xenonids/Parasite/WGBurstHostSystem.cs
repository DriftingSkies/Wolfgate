using Content.Shared._WG.Xenonids.Parasite;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;

namespace Content.Server._WG.Xenonids.Parasite;

/// <summary>When the player who burst takes their larva, their old body becomes unrepairable.</summary>
public sealed partial class WGBurstHostSystem : EntitySystem
{
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedXenoParasiteSystem _parasite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGBurstLarvaComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnMindAdded(Entity<WGBurstLarvaComponent> ent, ref MindAddedMessage args)
    {
        if (ent.Comp.Host is not { } host || TerminatingOrDeleted(host))
            return;

        if (!_mind.TryGetMind(ent, out var mindId, out var mind))
            return;

        if (_parasite.IsBurstHostPlayer(host, mindId, mind.UserId))
            _parasite.MarkBurstHostLeft(host);
    }
}
