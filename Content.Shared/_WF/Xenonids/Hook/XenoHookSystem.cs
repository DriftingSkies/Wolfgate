using Content.Shared._WF.RMCCore.Tether;
using Content.Shared._WF.Xenonids.TailSeize;
using Content.Shared.Throwing;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Xenonids.Hook;

public sealed partial class XenoHookSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;

    /// <summary>The longest a hook holds on: a pull lands well inside this, but in zero gravity it never lands.</summary>
    public static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(1.5);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoHookComponent, MoveEvent>(OnHookSourceMove);
        SubscribeLocalEvent<XenoHookComponent, EntityTerminatingEvent>(OnHookDelete);
        SubscribeLocalEvent<XenoHookedComponent, StopThrowEvent>(OnHookedStop);
        SubscribeLocalEvent<XenoHookedComponent, ComponentShutdown>(OnHookedRemoved);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // The release time isn't networked; the server lets go and the client follows its state.
        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<XenoHookedComponent>();
        while (query.MoveNext(out var uid, out var hooked))
        {
            if (now < hooked.ReleaseAt)
                continue;

            if (TryComp<XenoHookComponent>(hooked.Source, out var source))
                source.Hooked.Remove(uid);
            RemCompDeferred<XenoHookedComponent>(uid);
            // A pull that never landed gives no slow or root, and a later throw mustn't hand them out.
            RemCompDeferred<VictimTailSeizedComponent>(uid);
        }
    }

    private void OnHookSourceMove(Entity<XenoHookComponent> xeno, ref MoveEvent args)
    {
        if (xeno.Comp.Hooked.Count == 0)
            return;

        List<EntityUid> toRemove = new();

        foreach (var hooked in xeno.Comp.Hooked)
        {
            if (!TryComp<XenoHookedComponent>(hooked, out var hookComp))
            {
                toRemove.Add(hooked);
            }
        }

        foreach (var ent in toRemove)
        {
            xeno.Comp.Hooked.Remove(ent);
        }
    }

    private void OnHookDelete(Entity<XenoHookComponent> xeno, ref EntityTerminatingEvent args)
    {
        if (xeno.Comp.Hooked.Count == 0)
            return;

        foreach (var hooked in xeno.Comp.Hooked)
        {
            // A target since let go and hooked by someone else stays on the other hook.
            if (TryComp<XenoHookedComponent>(hooked, out var comp) && comp.Source == xeno.Owner)
                RemCompDeferred<XenoHookedComponent>(hooked);
        }

        xeno.Comp.Hooked.Clear();
    }

    private void OnHookedStop(Entity<XenoHookedComponent> ent, ref StopThrowEvent args)
    {
        RemCompDeferred<XenoHookedComponent>(ent);
    }

    private void OnHookedRemoved(Entity<XenoHookedComponent> ent, ref ComponentShutdown args)
    {
        RemCompDeferred<RMCTetherComponent>(ent);
    }

    public bool TryHookTarget(Entity<XenoHookComponent> xeno, EntityUid target)
    {
        //No double hooks
        if (HasComp<XenoHookedComponent>(target))
            return false;

        var hooked = EnsureComp<XenoHookedComponent>(target);
        hooked.ReleaseAt = _timing.CurTime + MaxHold;
        hooked.Source = xeno;
        xeno.Comp.Hooked.Add(target);
        Dirty(xeno);

        var tether = EnsureComp<RMCTetherComponent>(target);
        tether.TetherOrigin = xeno;
        Dirty(target, tether);

        return true;
    }
}
