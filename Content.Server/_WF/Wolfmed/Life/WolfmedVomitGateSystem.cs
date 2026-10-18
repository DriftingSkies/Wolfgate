using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>Raised on a body before <c>VomitSystem.Vomit</c> empties its stomach. Cancel to stop it.</summary>
[ByRefEvent]
public record struct WolfmedVomitAttemptEvent(bool Cancelled = false);

/// <summary>Nothing dead vomits, and neither does a wound host in cardiac arrest.</summary>
public sealed class WolfmedVomitGateSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MobStateComponent, WolfmedVomitAttemptEvent>(OnDeadVomitAttempt);
        SubscribeLocalEvent<WoundHostComponent, WolfmedVomitAttemptEvent>(OnVomitAttempt);
    }

    private void OnDeadVomitAttempt(Entity<MobStateComponent> ent, ref WolfmedVomitAttemptEvent args)
    {
        if (_mobState.IsDead(ent, ent.Comp))
            args.Cancelled = true;
    }

    private void OnVomitAttempt(Entity<WoundHostComponent> ent, ref WolfmedVomitAttemptEvent args)
    {
        if (HasComp<WolfmedCardiacArrestComponent>(ent))
            args.Cancelled = true;
    }
}
