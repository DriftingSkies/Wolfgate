using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Robust.Shared.Network;

namespace Content.Shared._WF.Xenonids.Parasite;

// WOLFGATE: a burst host is dead for good only if its player took the larva; otherwise surgeons can mend the chest
// and the body is an ordinary corpse again.
public abstract partial class SharedXenoParasiteSystem
{
    [Dependency] private SharedMindSystem _mind = default!;

    /// <summary>Records who the host was and which larva came out, so the two can be matched later.</summary>
    private void RecordBurstHost(Entity<VictimInfectedComponent> victim, VictimBurstComponent burst, EntityUid larva)
    {
        burst.HostUserId = victim.Comp.VictimUserId;
        burst.HostMind = _mind.GetMind(victim);
        Dirty(victim, burst);

        var link = EnsureComp<WFBurstLarvaComponent>(larva);
        link.Host = victim.Owner;

        // a queued player may already be in the larva before it bursts, so no MindAddedMessage will come
        if (_mind.TryGetMind(larva, out var mindId, out var mind) && IsBurstHostPlayer((victim.Owner, burst), mindId, mind.UserId))
            MarkBurstHostLeft((victim.Owner, burst));
    }

    /// <summary>The host's own player now drives the larva: the body cannot be mended any more.</summary>
    public void MarkBurstHostLeft(Entity<VictimBurstComponent?> host)
    {
        if (!Resolve(host, ref host.Comp, false))
            return;

        host.Comp.HostLeft = true;
        Dirty(host);
    }

    /// <summary>Whether this mind, or the player behind it, was the one bursting out of this host.</summary>
    public bool IsBurstHostPlayer(Entity<VictimBurstComponent?> host, EntityUid mindId, NetUserId? userId)
    {
        if (!Resolve(host, ref host.Comp, false))
            return false;

        return host.Comp.HostMind == mindId || userId != null && host.Comp.HostUserId == userId;
    }

    /// <summary>Test hook: sets the mind a burst host belonged to.</summary>
    public void SetBurstHostMind(Entity<VictimBurstComponent?> host, EntityUid? mindId)
    {
        if (!Resolve(host, ref host.Comp, false))
            return;

        host.Comp.HostMind = mindId;
        Dirty(host);
    }

    public bool CanRepairBurst(Entity<VictimBurstComponent?> host)
    {
        return Resolve(host, ref host.Comp, false) && !host.Comp.HostLeft;
    }

    /// <summary>First surgery step: the burst chest is cleared of what the larva left behind.</summary>
    public bool TryClearBurst(Entity<VictimBurstComponent?> host)
    {
        if (!Resolve(host, ref host.Comp, false) || host.Comp.HostLeft)
            return false;

        host.Comp.Cleared = true;
        Dirty(host);
        return true;
    }

    /// <summary>Final surgery step: the chest is mended and the body stops counting as burst.</summary>
    public bool TryRepairBurst(Entity<VictimBurstComponent?> host)
    {
        if (!Resolve(host, ref host.Comp, false) || host.Comp.HostLeft || !host.Comp.Cleared)
            return false;

        RemComp<VictimBurstComponent>(host);
        _appearance.RemoveData(host, BurstVisuals.Visuals);
        if (TryComp(host, out MobStateComponent? mobState))
            _mobState.UpdateMobState(host, mobState);

        return true;
    }
}
