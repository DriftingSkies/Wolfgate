using System.Numerics;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared._Goobstation.Wizard.Projectiles;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Buckle.Components;
using Content.Shared.Explosion;
using Content.Shared.Gravity;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Slippery;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Modsuits.Modules;

public sealed partial class ModSphereTransformModuleSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSphereTransformModuleComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ModSphereTransformModuleComponent, ModModuleActivateAttemptEvent>(OnActivateAttempt);
        SubscribeLocalEvent<ModSphereTransformModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModSphereTransformModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModSphereTransformModuleComponent, ModSuitWearerChangedEvent>(OnWearerChanged);
        SubscribeLocalEvent<ModSphereDrillComponent, ModModuleInstalledEvent>(OnDrillInstalled);
        SubscribeLocalEvent<ModSphereDrillComponent, ModModuleRemovedEvent>(OnDrillRemoved);
        SubscribeLocalEvent<ModSphereTransformModuleComponent, ModSphereMissileActionEvent>(OnMissileAction);
        SubscribeLocalEvent<ModMiningMissileComponent, ProjectileHitEvent>(OnMissileHit);
        SubscribeLocalEvent<ModSphereFormComponent, GetExplosionResistanceEvent>(OnExplosionResistance);
        SubscribeLocalEvent<ModSphereFormComponent, ComponentStartup>(OnFormStartup);
        SubscribeLocalEvent<ModSphereFormComponent, ComponentShutdown>(OnFormShutdown);
        SubscribeLocalEvent<ModSphereFormComponent, UpdateCanMoveEvent>(OnCanMove);
        SubscribeLocalEvent<ModSphereFormComponent, TileFrictionEvent>(OnTileFriction);
        SubscribeLocalEvent<ModSphereFormComponent, SlipAttemptEvent>(OnSlipAttempt);
        SubscribeLocalEvent<ModSphereFormComponent, KnockDownAttemptEvent>(OnKnockdownAttempt);
        SubscribeLocalEvent<ModSphereFormComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<ModSphereFormComponent, UseAttemptEvent>(OnUseAttempt);
        SubscribeLocalEvent<ModSphereFormComponent, PickupAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<ModSphereFormComponent, DropAttemptEvent>(OnDropAttempt);
        SubscribeLocalEvent<ModSphereFormComponent, ThrowAttemptEvent>(OnThrowAttempt);
        SubscribeLocalEvent<ModSphereFormComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<ModSphereFormComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ModSuitSystem _modSuit = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private const float MaxSteerOffset = 16f;

    private readonly List<Entity<ModSphereFormComponent>> _toDeactivate = new();
    private readonly HashSet<Entity<MobStateComponent>> _homingCandidates = new();
    private void OnMapInit(Entity<ModSphereTransformModuleComponent> ent, ref MapInitEvent args)
    {
        if (_actionContainer.EnsureAction(ent, ref ent.Comp.MissileActionEntity, ent.Comp.MissileAction))
            Dirty(ent);
    }
    private void OnActivateAttempt(Entity<ModSphereTransformModuleComponent> ent, ref ModModuleActivateAttemptEvent args)
    {
        if (args.User is not { } user)
            return;

        if (_gravity.IsWeightless(user))
        {
            args.Cancelled = true;
            args.Reason = "modsuit-sphere-no-gravity";
            return;
        }

        if (TryComp<BuckleComponent>(user, out var buckle) && buckle.Buckled)
        {
            args.Cancelled = true;
            args.Reason = "modsuit-sphere-buckled";
        }
    }
    private void OnActivated(Entity<ModSphereTransformModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (args.Wearer is { } wearer)
            ApplyForm(ent, wearer, args.Suit);
    }
    private void OnDeactivated(Entity<ModSphereTransformModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        RevertForm(ent, true);
    }
    private void OnWearerChanged(Entity<ModSphereTransformModuleComponent> ent, ref ModSuitWearerChangedEvent args)
    {
        if (!_timing.ApplyingState
            && TryComp<ModModuleComponent>(ent, out var moduleComp)
            && moduleComp.Active
            && _modSuit.TryGetModuleSuit(ent, out var suit))
            _modSuit.SetModuleActive(suit, (ent.Owner, moduleComp), false);

        RevertForm(ent, false);
    }

    private void ApplyForm(Entity<ModSphereTransformModuleComponent> ent, EntityUid wearer, EntityUid suit)
    {
        if (_timing.ApplyingState || TerminatingOrDeleted(wearer))
            return;

        var form = EnsureComp<ModSphereFormComponent>(wearer);
        form.Module = ent;
        form.Drill = FindDrill(suit);
        form.PrimaryColor = ent.Comp.PrimaryColor;
        form.DarkColor = ent.Comp.DarkColor;
        form.AccentColor = ent.Comp.AccentColor;
        form.SteerOffset = Vector2.Zero;
        form.Thrusting = false;
        form.Hits.Clear();
        Dirty(wearer, form);

        ent.Comp.FormOwner = wearer;
        Dirty(ent);

        if (ent.Comp.MissileActionEntity is { } missileAction
            && _actions.TryGetActionData(missileAction, out _, false))
            _actions.AddAction(wearer, missileAction, ent.Owner);

        _audio.PlayPredicted(ent.Comp.ActivateSound, wearer, wearer);
    }

    private void RevertForm(Entity<ModSphereTransformModuleComponent> ent, bool playSound)
    {
        if (_timing.ApplyingState || ent.Comp.FormOwner is not { } uid)
            return;

        ent.Comp.FormOwner = null;
        ent.Comp.PendingMissiles = 0;
        Dirty(ent);

        if (TerminatingOrDeleted(uid))
            return;

        if (ent.Comp.MissileActionEntity is { } missileActionId
            && _actions.TryGetActionData(missileActionId, out var missileAction, false)
            && missileAction.AttachedEntity == uid)
            _actions.RemoveProvidedAction(uid, ent, missileActionId);

        if (playSound)
            _audio.PlayPredicted(ent.Comp.DeactivateSound, uid, uid);

        if (TryComp<ModSphereFormComponent>(uid, out var form) && form.Module == ent.Owner)
            RemCompDeferred(uid, form);
    }

    private EntityUid? FindDrill(EntityUid suit)
    {
        if (!TryComp<ModSuitComponent>(suit, out var suitComp))
            return null;

        foreach (var module in suitComp.ModulesContainer.ContainedEntities)
        {
            if (HasComp<ModSphereDrillComponent>(module))
                return module;
        }

        return null;
    }
    private void OnDrillInstalled(Entity<ModSphereDrillComponent> ent, ref ModModuleInstalledEvent args)
    {
        RefreshDrill(args.Suit);
    }
    private void OnDrillRemoved(Entity<ModSphereDrillComponent> ent, ref ModModuleRemovedEvent args)
    {
        RefreshDrill(args.Suit);
    }

    private void RefreshDrill(EntityUid suit)
    {
        if (_timing.ApplyingState
            || !TryComp<ModSuitComponent>(suit, out var suitComp)
            || suitComp.Wearer is not { } wearer
            || !TryComp<ModSphereFormComponent>(wearer, out var form))
            return;

        form.Drill = FindDrill(suit);
        Dirty(wearer, form);
    }
    private void OnMissileAction(Entity<ModSphereTransformModuleComponent> ent, ref ModSphereMissileActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        if (!TryComp<ModSphereFormComponent>(user, out var form) || form.Module != ent.Owner
            || !_modSuit.TryGetModuleSuit(ent, out var suit))
            return;

        if (!_modSuit.TryUseCharge(suit, ent.Comp.MissilePower))
        {
            _popup.PopupClient(Loc.GetString("modsuit-no-power"), user, user);
            return;
        }

        args.Handled = true;
        if (_net.IsClient)
            return;

        ent.Comp.PendingMissiles = ent.Comp.MissileCount;
        ent.Comp.NextMissile = _timing.CurTime;
        ent.Comp.MissileTarget = _transform.ToMapCoordinates(args.Target);
    }
    private void OnMissileHit(Entity<ModMiningMissileComponent> ent, ref ProjectileHitEvent args)
    {
        if (HasComp<MobStateComponent>(args.Target))
            args.Damage *= ent.Comp.FaunaMultiplier;
    }
    private void OnExplosionResistance(Entity<ModSphereFormComponent> ent, ref GetExplosionResistanceEvent args)
    {
        if (TryComp<ModSphereTransformModuleComponent>(ent.Comp.Module, out var module))
            args.DamageCoefficient *= module.ExplosionCoefficient;
    }
    private void OnFormStartup(Entity<ModSphereFormComponent> ent, ref ComponentStartup args)
    {
        _actionBlocker.UpdateCanMove(ent);
    }
    private void OnFormShutdown(Entity<ModSphereFormComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            _actionBlocker.UpdateCanMove(ent);
    }
    private void OnCanMove(Entity<ModSphereFormComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.LifeStage <= ComponentLifeStage.Running)
            args.Cancel();
    }
    private void OnTileFriction(Entity<ModSphereFormComponent> ent, ref TileFrictionEvent args)
    {
        args.Modifier = 0f;
    }
    private void OnSlipAttempt(Entity<ModSphereFormComponent> ent, ref SlipAttemptEvent args)
    {
        args.NoSlip = true;
    }
    private void OnKnockdownAttempt(Entity<ModSphereFormComponent> ent, ref KnockDownAttemptEvent args)
    {
        args.Cancelled = true;
    }
    private void OnInteractionAttempt(Entity<ModSphereFormComponent> ent, ref InteractionAttemptEvent args)
    {
        if (args.Target != null)
            args.Cancelled = true;
    }
    private void OnUseAttempt(Entity<ModSphereFormComponent> ent, ref UseAttemptEvent args)
    {
        args.Cancel();
    }
    private void OnPickupAttempt(Entity<ModSphereFormComponent> ent, ref PickupAttemptEvent args)
    {
        args.Cancel();
    }
    private void OnDropAttempt(Entity<ModSphereFormComponent> ent, ref DropAttemptEvent args)
    {
        args.Cancel();
    }
    private void OnThrowAttempt(Entity<ModSphereFormComponent> ent, ref ThrowAttemptEvent args)
    {
        args.Cancel();
    }
    private void OnAttackAttempt(Entity<ModSphereFormComponent> ent, ref AttackAttemptEvent args)
    {
        args.Cancel();
    }
    private void OnMobStateChanged(Entity<ModSphereFormComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Deactivate(ent);
    }

    [EventSubscription]
    private void OnSteer(ModSphereSteerEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid || !TryComp<ModSphereFormComponent>(uid, out var form))
            return;

        var offset = msg.Offset;
        if (!float.IsFinite(offset.X) || !float.IsFinite(offset.Y))
            offset = Vector2.Zero;

        if (offset.LengthSquared() > MaxSteerOffset * MaxSteerOffset)
            offset = offset.Normalized() * MaxSteerOffset;

        form.SteerOffset = offset;
        form.Thrusting = msg.Thrusting;
        Dirty(uid, form);
    }

    private void Deactivate(Entity<ModSphereFormComponent> ent)
    {
        if (_timing.ApplyingState
            || ent.Comp.Module is not { } module
            || !TryComp<ModModuleComponent>(module, out var moduleComp)
            || !_modSuit.TryGetModuleSuit(module, out var suit))
            return;

        _modSuit.SetModuleActive(suit, (module, moduleComp), false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_net.IsClient)
            return;

        _toDeactivate.Clear();
        var forms = EntityQueryEnumerator<ModSphereFormComponent>();
        while (forms.MoveNext(out var uid, out var form))
        {
            if (_gravity.IsWeightless(uid)
                || TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled
                || _mobState.IsIncapacitated(uid))
                _toDeactivate.Add((uid, form));
        }

        foreach (var ent in _toDeactivate)
        {
            Deactivate(ent);
        }

        var now = _timing.CurTime;
        var modules = EntityQueryEnumerator<ModSphereTransformModuleComponent>();
        while (modules.MoveNext(out var uid, out var module))
        {
            if (module.PendingMissiles <= 0 || now < module.NextMissile)
                continue;

            if (!TryGetFormOwner((uid, module), out var owner))
            {
                module.PendingMissiles = 0;
                continue;
            }

            module.PendingMissiles--;
            module.NextMissile = now + module.MissileInterval;
            FireMissile((uid, module), owner);
        }
    }

    private bool TryGetFormOwner(Entity<ModSphereTransformModuleComponent> module, out EntityUid owner)
    {
        owner = default;
        if (module.Comp.FormOwner is not { } wearer
            || !TryComp<ModSphereFormComponent>(wearer, out var form)
            || form.Module != module.Owner)
            return false;

        owner = wearer;
        return true;
    }

    private EntityUid? FindHomingTarget(Entity<ModSphereTransformModuleComponent> ent, EntityUid owner)
    {
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        _homingCandidates.Clear();
        _lookup.GetEntitiesInRange(ent.Comp.MissileTarget, ent.Comp.MissileHomingRange, _homingCandidates);
        foreach (var mob in _homingCandidates)
        {
            if (mob.Owner == owner || !_mobState.IsAlive(mob, mob.Comp))
                continue;

            var distance = (_transform.GetMapCoordinates(mob).Position - ent.Comp.MissileTarget.Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            best = mob;
            bestDistance = distance;
        }

        return best;
    }

    private void FireMissile(Entity<ModSphereTransformModuleComponent> ent, EntityUid owner)
    {
        var from = _transform.GetMapCoordinates(owner);
        if (from.MapId != ent.Comp.MissileTarget.MapId)
            return;

        var direction = ent.Comp.MissileTarget.Position - from.Position;
        if (direction.LengthSquared() < 0.01f)
            return;

        var missile = Spawn(ent.Comp.MissileProjectile, from);
        _gun.ShootProjectile(missile, direction, _physics.GetMapLinearVelocity(owner), ent, owner, ent.Comp.MissileSpeed);

        if (TryComp<HomingProjectileComponent>(missile, out var homing) && FindHomingTarget(ent, owner) is { } target)
        {
            homing.Target = target;
            Dirty(missile, homing);
        }
        _audio.PlayPvs(ent.Comp.MissileSound, owner);
    }
}
