using Content.Server.Popups;
using Content.Server.Wires;
using Content.Shared._WF.Warheads;
using Content.Shared.Wires;

namespace Content.Server._WF.Warheads;

/// <summary>
/// One of a warhead's two fuze wires. Cutting it while the warhead is armed either makes it safe or sets it off; which
/// wire is which is hidden behind the panel's random colours.
/// </summary>
public abstract partial class WFWarheadFuzeWireAction : BaseWireAction
{
    public override string Name { get; set; } = "wf-warhead-wire-name";

    public override Color Color { get; set; } = Color.Red;

    public override object? StatusKey => null;

    public override StatusLightData? GetStatusLightData(Wire wire) => null;

    public override bool Cut(EntityUid user, Wire wire)
    {
        base.Cut(user, wire);

        if (!EntityManager.TryGetComponent<WFWarheadComponent>(wire.Owner, out var warhead)
            || warhead.State == WFWarheadState.Safe)
            return true;

        OnArmedCut(user, (wire.Owner, warhead));
        return true;
    }

    /// <summary>What cutting this wire does to an armed warhead.</summary>
    protected abstract void OnArmedCut(EntityUid user, Entity<WFWarheadComponent> warhead);
}

/// <summary>The right wire: the countdown stops and the safety goes back on, even in the final seconds.</summary>
public sealed partial class WFWarheadDisarmWireAction : WFWarheadFuzeWireAction
{
    protected override void OnArmedCut(EntityUid user, Entity<WFWarheadComponent> warhead)
    {
        EntityManager.System<WFWarheadSystem>().Disarm(warhead, user, force: true);
        EntityManager.System<PopupSystem>().PopupEntity(Loc.GetString("wf-warhead-wire-disarmed"), warhead, user);
    }
}

/// <summary>The wrong wire: the warhead goes off at once.</summary>
public sealed partial class WFWarheadDetonateWireAction : WFWarheadFuzeWireAction
{
    protected override void OnArmedCut(EntityUid user, Entity<WFWarheadComponent> warhead)
    {
        var warheads = EntityManager.System<WFWarheadSystem>();
        if (warheads.GetPayload(warhead.Comp) is { } payload)
            warheads.Detonate(warhead, payload, user);
    }
}
