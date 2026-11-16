using Content.Shared.ActionBlocker;
using Content.Shared.Buckle.Components;
using Content.Shared.Buckle;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Modsuits.Support;

/// <summary>Decides when a <see cref="WaddleAnimationComponent"/> should animate; the client draws it.</summary>
public abstract partial class SharedWaddleAnimationSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WaddleAnimationComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<WaddleAnimationComponent, MoveInputEvent>(OnMovementInput);
        SubscribeLocalEvent<WaddleAnimationComponent, StoodEvent>(OnStood);
        SubscribeLocalEvent<WaddleAnimationComponent, StunnedEvent>(OnStunned);
        SubscribeLocalEvent<WaddleAnimationComponent, DownedEvent>(OnDowned);
        SubscribeLocalEvent<WaddleAnimationComponent, BuckledEvent>(OnBuckled);
        SubscribeLocalEvent<WaddleAnimationComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<WaddleAnimationComponent, GravityChangedEvent>(OnGravityChanged);
    }

    private void OnGravityChanged(Entity<WaddleAnimationComponent> ent, ref GravityChangedEvent args)
    {
        if (!args.HasGravity)
            SetWaddling(ent, false);
    }

    private void OnComponentStartup(Entity<WaddleAnimationComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<InputMoverComponent>(ent, out var mover))
            return;

        if ((mover.HeldMoveButtons & MoveButtons.AnyDirection) != MoveButtons.None)
            SetWaddling(ent, true);
    }

    private void OnMovementInput(Entity<WaddleAnimationComponent> ent, ref MoveInputEvent args)
    {
        SetWaddling(ent, args.HasDirectionalMovement);
    }

    private void OnStood(Entity<WaddleAnimationComponent> ent, ref StoodEvent args)
    {
        if (!TryComp<InputMoverComponent>(ent, out var mover))
            return;

        if ((mover.HeldMoveButtons & MoveButtons.AnyDirection) == MoveButtons.None)
            return;

        SetWaddling(ent, true);
    }

    private void OnStunned(Entity<WaddleAnimationComponent> ent, ref StunnedEvent args) => SetWaddling(ent, false);

    private void OnDowned(Entity<WaddleAnimationComponent> ent, ref DownedEvent args) => SetWaddling(ent, false);

    private void OnBuckled(Entity<WaddleAnimationComponent> ent, ref BuckledEvent args) => SetWaddling(ent, false);

    private void OnMobStateChanged(Entity<WaddleAnimationComponent> ent, ref MobStateChangedEvent args) => SetWaddling(ent, false);

    /// <summary>Enables or disables waddling, including the animation.</summary>
    public void SetWaddling(Entity<WaddleAnimationComponent> ent, bool waddling, bool force = false)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (waddling && !force && !CanWaddle(ent))
            waddling = false;

        if (ent.Comp.IsWaddling == waddling)
            return;

        ent.Comp.IsWaddling = waddling;
        DirtyField(ent, ent.Comp, nameof(WaddleAnimationComponent.IsWaddling));
        UpdateAnimation(ent);
    }

    /// <summary>Returns true if an entity is allowed to waddle at all.</summary>
    public bool CanWaddle(EntityUid uid)
    {
        return _mob.IsAlive(uid)
               && !_gravity.IsWeightless(uid)
               && _actionBlocker.CanMove(uid)
               && !_buckle.IsBuckled(uid)
               && !_standing.IsDown(uid)
               && Transform(uid).GridUid != null;
    }

    /// <summary>Updates the waddling animation. Does nothing on the server.</summary>
    protected virtual void UpdateAnimation(Entity<WaddleAnimationComponent> ent)
    {
    }
}
