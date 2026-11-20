using Content.Shared._WG.Augments;
using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Content.Shared.Emp;
using Content.Shared.Input;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WG.Augments;

/// <summary>
/// Works a shoulder cannon: its action raises it over the shoulder or folds it away, and while it is up a middle click
/// starts the charge, after which a bolt flies at the spot that was clicked. Firing drops optical camouflage, and an
/// EMP folds the cannon away and locks the action for a while.
/// </summary>
public sealed class WGShoulderCannonSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private WGOpticalCamoSystem _camo = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShoulderCannonComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<WGShoulderCannonComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WGShoulderCannonComponent, WGToggleShoulderCannonEvent>(OnToggle);
        SubscribeLocalEvent<WGShoulderCannonComponent, WGShoulderCannonDoAfterEvent>(OnCharged);
        SubscribeLocalEvent<WGShoulderCannonComponent, EmpPulseEvent>(OnEmp);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.MouseMiddle, new PointerInputCmdHandler(OnMiddleClick))
            .Register<WGShoulderCannonSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<WGShoulderCannonSystem>();
    }

    private void OnInit(Entity<WGShoulderCannonComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent, ref ent.Comp.DeployActionEntity, ent.Comp.DeployAction);
    }

    private void OnShutdown(Entity<WGShoulderCannonComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            RemComp<WGShoulderCannonDeployedComponent>(ent);

        QueueDel(ent.Comp.DeployActionEntity);
        ent.Comp.DeployActionEntity = null;
    }

    private void OnToggle(Entity<WGShoulderCannonComponent> ent, ref WGToggleShoulderCannonEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        SetDeployed(ent, !HasComp<WGShoulderCannonDeployedComponent>(ent));
    }

    /// <summary>Raises the cannon over the shoulder or folds it away.</summary>
    public void SetDeployed(Entity<WGShoulderCannonComponent> ent, bool deployed)
    {
        if (deployed == HasComp<WGShoulderCannonDeployedComponent>(ent))
            return;

        if (deployed)
            EnsureComp<WGShoulderCannonDeployedComponent>(ent);
        else
            RemComp<WGShoulderCannonDeployedComponent>(ent);

        _actions.SetToggled(ent.Comp.DeployActionEntity, deployed);
        _audio.PlayPvs(deployed ? ent.Comp.DeploySound : ent.Comp.StowSound, ent);
    }

    private bool OnMiddleClick(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        // At a console, a middle click pans the view instead.
        if (session?.AttachedEntity is { } body
            && !HasComp<PilotComponent>(body)
            && TryComp<WGShoulderCannonComponent>(body, out var cannon)
            && coords.IsValid(EntityManager))
            TryCharge((body, cannon), coords);

        // Other middle-click binds still get the click.
        return false;
    }

    /// <summary>Starts charging the raised cannon at a spot, if it isn't folded away, charging or cooling down.</summary>
    public bool TryCharge(Entity<WGShoulderCannonComponent> ent, EntityCoordinates target)
    {
        if (!TryComp<WGShoulderCannonDeployedComponent>(ent, out var deployed) || deployed.Charging)
            return false;

        if (_timing.CurTime < ent.Comp.NextFire)
        {
            _popup.PopupEntity(Loc.GetString("wg-shoulder-cannon-cooling"), ent, ent);
            return false;
        }

        var doAfter = new DoAfterArgs(EntityManager, ent, ent.Comp.ChargeTime,
            new WGShoulderCannonDoAfterEvent(GetNetCoordinates(target)), ent)
        {
            BreakOnDamage = false,
            BreakOnMove = false,
            NeedHand = false,
            RequireCanInteract = true,
        };
        if (!_doAfter.TryStartDoAfter(doAfter))
            return false;

        ent.Comp.NextFire = _timing.CurTime + ent.Comp.FireDelay;
        SetCharging((ent, deployed), true);
        _audio.PlayPvs(ent.Comp.ChargeSound, ent);
        return true;
    }

    private void OnCharged(Entity<WGShoulderCannonComponent> ent, ref WGShoulderCannonDoAfterEvent args)
    {
        // Folded away mid-charge, the shot is lost.
        if (!TryComp<WGShoulderCannonDeployedComponent>(ent, out var deployed))
            return;

        SetCharging((ent, deployed), false);
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        Fire(ent, GetCoordinates(args.Target));
    }

    private void SetCharging(Entity<WGShoulderCannonDeployedComponent> ent, bool charging)
    {
        ent.Comp.Charging = charging;
        Dirty(ent);
    }

    /// <summary>Throws a bolt from the body at the given spot.</summary>
    public EntityUid? Fire(Entity<WGShoulderCannonComponent> ent, EntityCoordinates target)
    {
        var from = _transform.GetMapCoordinates(ent);
        var to = _transform.ToMapCoordinates(target);
        if (from.MapId != to.MapId)
            return null;

        var direction = to.Position - from.Position;
        if (direction.LengthSquared() < 0.01f)
            direction = _transform.GetWorldRotation(ent).ToWorldVec();

        var bolt = Spawn(ent.Comp.Bolt, Transform(ent).Coordinates);
        _gun.ShootProjectile(bolt, direction, _physics.GetMapLinearVelocity(ent), ent, ent, ent.Comp.BoltSpeed);
        _audio.PlayPvs(ent.Comp.FireSound, ent);
        _camo.Reveal(ent.Owner);
        return bolt;
    }

    private void OnEmp(Entity<WGShoulderCannonComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        SetDeployed(ent, false);
        _actions.SetCooldown(ent.Comp.DeployActionEntity, ent.Comp.EmpLockout);
    }
}
