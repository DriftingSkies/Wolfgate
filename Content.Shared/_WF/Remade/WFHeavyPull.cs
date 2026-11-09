using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Remade;

/// <summary>Something so heavy that whoever pulls it is slowed to this share of their speed.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFHeavyPullComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Multiplier = 0.35f;
}

/// <summary>On whoever is pulling something heavy: the share of their speed left to them.</summary>
[RegisterComponent]
public sealed partial class WFHeavyPullingComponent : Component
{
    [ViewVariables]
    public float Multiplier = 1f;
}

/// <summary>Slows whoever pulls a <see cref="WFHeavyPullComponent"/>, and lets them go again when they stop.</summary>
public sealed class WFHeavyPullSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFHeavyPullComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<WFHeavyPullComponent, PullStoppedMessage>(OnPullStopped);
        SubscribeLocalEvent<WFHeavyPullingComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnPullStarted(Entity<WFHeavyPullComponent> ent, ref PullStartedMessage args)
    {
        if (args.PulledUid != ent.Owner)
            return;

        EnsureComp<WFHeavyPullingComponent>(args.PullerUid).Multiplier = ent.Comp.Multiplier;
        _speed.RefreshMovementSpeedModifiers(args.PullerUid);
    }

    private void OnPullStopped(Entity<WFHeavyPullComponent> ent, ref PullStoppedMessage args)
    {
        if (args.PulledUid != ent.Owner || !RemComp<WFHeavyPullingComponent>(args.PullerUid))
            return;

        _speed.RefreshMovementSpeedModifiers(args.PullerUid);
    }

    private void OnRefreshSpeed(Entity<WFHeavyPullingComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.Multiplier, ent.Comp.Multiplier);
    }
}
