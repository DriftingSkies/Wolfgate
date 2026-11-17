using Content.Shared.Movement.Pulling.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Remade;

/// <summary>Something so heavy that whoever pulls it is slowed to this share of their speed.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGHeavyPullComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Multiplier = 0.35f;
}

/// <summary>On whoever is pulling something heavy: the share of their speed left to them.</summary>
[RegisterComponent]
public sealed partial class WGHeavyPullingComponent : Component
{
    [ViewVariables]
    public float Multiplier = 1f;
}

/// <summary>Slows whoever pulls a <see cref="WGHeavyPullComponent"/>, and lets them go again when they stop.</summary>
public sealed class WGHeavyPullSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _speed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGHeavyPullComponent, PullStartedMessage>(OnPullStarted);
        SubscribeLocalEvent<WGHeavyPullComponent, PullStoppedMessage>(OnPullStopped);
        SubscribeLocalEvent<WGHeavyPullingComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnPullStarted(Entity<WGHeavyPullComponent> ent, ref PullStartedMessage args)
    {
        if (args.PulledUid != ent.Owner)
            return;

        EnsureComp<WGHeavyPullingComponent>(args.PullerUid).Multiplier = ent.Comp.Multiplier;
        _speed.RefreshMovementSpeedModifiers(args.PullerUid);
    }

    private void OnPullStopped(Entity<WGHeavyPullComponent> ent, ref PullStoppedMessage args)
    {
        if (args.PulledUid != ent.Owner || !RemComp<WGHeavyPullingComponent>(args.PullerUid))
            return;

        _speed.RefreshMovementSpeedModifiers(args.PullerUid);
    }

    private void OnRefreshSpeed(Entity<WGHeavyPullingComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.Multiplier, ent.Comp.Multiplier);
    }
}
