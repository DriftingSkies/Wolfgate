using Content.Server.Popups;
using Content.Server.Wires;
using Content.Shared._WG.Warheads;
using Content.Shared.Wires;

namespace Content.Server._WG.Warheads;

/// <summary>
/// One of a warhead's two fuze wires. Cutting it while the warhead is armed either makes it safe or sets it off; which
/// wire is which is hidden behind the panel's random colours.
/// </summary>
public abstract partial class WGWarheadFuzeWireAction : BaseWireAction
{
    public override string Name { get; set; } = "wg-warhead-wire-name";

    public override Color Color { get; set; } = Color.Red;

    public override object? StatusKey => null;

    public override StatusLightData? GetStatusLightData(Wire wire) => null;

    public override bool Cut(EntityUid user, Wire wire)
    {
        base.Cut(user, wire);

        if (!EntityManager.TryGetComponent<WGWarheadComponent>(wire.Owner, out var warhead)
            || warhead.State == WGWarheadState.Safe)
            return true;

        OnArmedCut(user, (wire.Owner, warhead));
        return true;
    }

    /// <summary>What cutting this wire does to an armed warhead.</summary>
    protected abstract void OnArmedCut(EntityUid user, Entity<WGWarheadComponent> warhead);
}

/// <summary>The right wire: the countdown stops and the safety goes back on, even in the final seconds.</summary>
public sealed partial class WGWarheadDisarmWireAction : WGWarheadFuzeWireAction
{
    protected override void OnArmedCut(EntityUid user, Entity<WGWarheadComponent> warhead)
    {
        EntityManager.System<WGWarheadSystem>().Disarm(warhead, user, force: true);
        EntityManager.System<PopupSystem>().PopupEntity(Loc.GetString("wg-warhead-wire-disarmed"), warhead, user);
    }
}

/// <summary>The wrong wire: the warhead goes off at once.</summary>
public sealed partial class WGWarheadDetonateWireAction : WGWarheadFuzeWireAction
{
    protected override void OnArmedCut(EntityUid user, Entity<WGWarheadComponent> warhead)
    {
        var warheads = EntityManager.System<WGWarheadSystem>();
        if (warheads.GetPayload(warhead.Comp) is { } payload)
            warheads.Detonate(warhead, payload, user);
    }
}
