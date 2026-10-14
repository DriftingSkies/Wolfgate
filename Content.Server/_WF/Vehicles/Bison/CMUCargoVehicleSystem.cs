using Content.Server._ES.TileFires;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Mind;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Bison;
using Content.Shared._WF.Vehicles.Falling;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.SSDIndicator;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Bison;

/// <summary>Runs the Bison remote cargo carrier: pairing, remote driving by mind visit, the cargo bay and the self-destruct.</summary>
public sealed partial class CMUCargoVehicleSystem : EntitySystem
{
    private static readonly TimeSpan SessionValidationInterval = TimeSpan.FromSeconds(0.25);

    private const string ReturnActionId = "CMUActionCargoVehicleReturn";
    private const string SelfDestructActionId = "CMUActionCargoVehicleSelfDestruct";
    private const string ToggleBayActionId = "CMUActionCargoVehicleToggleBay";

    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private ESTileFireSystem _tileFire = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TriggerSystem _trigger = default!;
    [Dependency] private RMCVehicleSystem _vehicles = default!;

    private TimeSpan _nextSessionValidation;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMUCargoVehicleComponent, ComponentInit>(OnVehicleInit);
        SubscribeLocalEvent<CMUCargoVehicleComponent, MapInitEvent>(OnVehicleMapInit);
        SubscribeLocalEvent<CMUCargoVehicleComponent, DamageChangedEvent>(OnVehicleDamageChanged);
        SubscribeLocalEvent<CMUCargoVehicleComponent, ComponentShutdown>(OnVehicleShutdown);
        SubscribeLocalEvent<CMUCargoVehicleComponent, EntityTerminatingEvent>(OnVehicleTerminating, before: new[] { typeof(MindSystem) });
        SubscribeLocalEvent<CMUCargoVehicleComponent, GetVerbsEvent<AlternativeVerb>>(OnVehicleGetVerbs);
        SubscribeLocalEvent<CMUCargoVehicleComponent, InteractUsingEvent>(OnVehicleInteractUsing);
        SubscribeLocalEvent<CMUCargoVehicleComponent, CMUCargoVehicleLoadDoAfterEvent>(OnLoadDoAfter);
        SubscribeLocalEvent<CMUCargoVehicleComponent, CMUCargoVehicleUnloadDoAfterEvent>(OnUnloadDoAfter);
        SubscribeLocalEvent<CMUCargoVehicleComponent, TriggerEvent>(OnVehicleTriggered, before: new[] { typeof(TriggerSystem) });

        SubscribeLocalEvent<CMUCargoVehicleControllerComponent, ComponentShutdown>(OnControllerShutdown);
        SubscribeLocalEvent<CMUCargoVehicleControllerComponent, EntityTerminatingEvent>(OnControllerTerminating, before: new[] { typeof(MindSystem) });
        SubscribeLocalEvent<CMUCargoVehicleControllerComponent, GotUnequippedHandEvent>(OnControllerUnequipped);
        SubscribeLocalEvent<CMUCargoVehicleControllerComponent, UseInHandEvent>(OnControllerUseInHand);

        SubscribeLocalEvent<CMUCargoVehicleControlSessionComponent, VehicleFellEvent>(OnVehicleFell);
        SubscribeLocalEvent<CMUCargoVehicleControlSessionComponent, CMUCargoVehicleReturnActionEvent>(OnReturnAction);
        SubscribeLocalEvent<CMUCargoVehicleControlSessionComponent, CMUCargoVehicleSelfDestructActionEvent>(OnSelfDestructAction);
        SubscribeLocalEvent<CMUCargoVehicleControlSessionComponent, CMUCargoVehicleToggleBayActionEvent>(OnToggleBayAction);
        SubscribeLocalEvent<CMUCargoVehicleControlSessionComponent, PlayerDetachedEvent>(OnVehiclePlayerDetached, after: new[] { typeof(SSDIndicatorSystem) });

        SubscribeLocalEvent<CMUCargoVehicleRemotePilotComponent, MobStateChangedEvent>(OnOperatorMobStateChanged);
        SubscribeLocalEvent<CMUCargoVehicleRemotePilotComponent, ComponentShutdown>(OnRemotePilotShutdown);
        SubscribeLocalEvent<CMUCargoVehicleRemotePilotComponent, PlayerAttachedEvent>(OnRemotePilotPlayerAttached, after: new[] { typeof(SSDIndicatorSystem) });
        SubscribeLocalEvent<CMUCargoVehicleRemotePilotComponent, PlayerDetachedEvent>(OnRemotePilotPlayerDetached, after: new[] { typeof(SSDIndicatorSystem) });
        // The mind system turns a mind that is visiting something into that something's owner when its body is deleted, so
        // the visit has to end first. A system's subscriptions to one event share their ordering, hence all three.
        SubscribeLocalEvent<CMUCargoVehicleRemotePilotComponent, EntityTerminatingEvent>(OnRemotePilotTerminating, before: new[] { typeof(MindSystem) });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextSessionValidation)
            return;

        _nextSessionValidation = _timing.CurTime + SessionValidationInterval;

        var query = EntityQueryEnumerator<CMUCargoVehicleControlSessionComponent>();
        while (query.MoveNext(out var vehicle, out var session))
        {
            if (!ValidateSession((vehicle, session), out var reason))
                EndControl((vehicle, session), reason);
        }
    }

    private void OnVehicleInit(Entity<CMUCargoVehicleComponent> ent, ref ComponentInit args)
    {
        ent.Comp.CargoContainer = _containers.EnsureContainer<ContainerSlot>(ent.Owner, ent.Comp.CargoContainerId);

        UpdateBayAppearance(ent);
    }

    private void OnVehicleMapInit(Entity<CMUCargoVehicleComponent> ent, ref MapInitEvent args)
    {
        if (TryComp(ent.Owner, out RMCVehicleComponent? vehicle))
            _vehicles.TrySetOperator((ent.Owner, vehicle), ent.Owner);
    }

    private void OnVehicleDamageChanged(Entity<CMUCargoVehicleComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.ArmingMode != CMUCargoVehicleArmingMode.None ||
            args.Damageable.TotalDamage < ent.Comp.AutomaticArmDamage)
        {
            return;
        }

        Arm(ent, CMUCargoVehicleArmingMode.Automatic, null);
    }

    private void OnVehicleShutdown(Entity<CMUCargoVehicleComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp(ent.Owner, out CMUCargoVehicleControlSessionComponent? session))
            EndControl((ent.Owner, session), Loc.GetString("cmu-cargo-vehicle-control-ended-vehicle-lost"));

        UnlinkController(ent);
    }

    private void OnVehicleTerminating(Entity<CMUCargoVehicleComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp(ent.Owner, out CMUCargoVehicleControlSessionComponent? session))
            EndControl((ent.Owner, session), Loc.GetString("cmu-cargo-vehicle-control-ended-vehicle-lost"));

        UnlinkController(ent);
    }

    private void OnVehicleInteractUsing(Entity<CMUCargoVehicleComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp(args.Used, out CMUCargoVehicleControllerComponent? controller))
            return;

        args.Handled = true;
        PairController(ent, (args.Used, controller), args.User);
    }

    private void OnControllerShutdown(Entity<CMUCargoVehicleControllerComponent> ent, ref ComponentShutdown args)
    {
        EndControlForController(ent.Owner, Loc.GetString("cmu-cargo-vehicle-control-ended-controller-lost"));
        UnlinkVehicle(ent);
    }

    private void OnControllerTerminating(Entity<CMUCargoVehicleControllerComponent> ent, ref EntityTerminatingEvent args)
    {
        EndControlForController(ent.Owner, Loc.GetString("cmu-cargo-vehicle-control-ended-controller-lost"));
        UnlinkVehicle(ent);
    }

    private void OnControllerUnequipped(Entity<CMUCargoVehicleControllerComponent> ent, ref GotUnequippedHandEvent args)
    {
        EndControlForController(ent.Owner, Loc.GetString("cmu-cargo-vehicle-control-ended-controller-dropped"));
    }

    private void OnControllerUseInHand(Entity<CMUCargoVehicleControllerComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (TryGetSessionForController(ent.Owner, out var session))
        {
            EndControl(session, Loc.GetString("cmu-cargo-vehicle-control-ended-manual"));
            return;
        }

        StartControl(ent, args.User);
    }

    private void PairController(
        Entity<CMUCargoVehicleComponent> vehicle,
        Entity<CMUCargoVehicleControllerComponent> controller,
        EntityUid user)
    {
        if (vehicle.Comp.ArmingMode != CMUCargoVehicleArmingMode.None ||
            HasComp<CMUCargoVehicleControlSessionComponent>(vehicle.Owner))
        {
            Popup(user, "cmu-cargo-vehicle-pair-busy");
            return;
        }

        if (!_hands.IsHolding(user, controller.Owner))
        {
            Popup(user, "cmu-cargo-vehicle-controller-must-hold");
            return;
        }

        if (controller.Comp.LinkedVehicle is { } previous && previous != vehicle.Owner &&
            TryComp(previous, out CMUCargoVehicleComponent? previousVehicle))
        {
            if (previousVehicle.ArmingMode != CMUCargoVehicleArmingMode.None ||
                HasComp<CMUCargoVehicleControlSessionComponent>(previous))
            {
                Popup(user, "cmu-cargo-vehicle-pair-busy");
                return;
            }

            previousVehicle.Controller = null;
            Dirty(previous, previousVehicle);
        }

        if (vehicle.Comp.Controller is { } oldController && oldController != controller.Owner &&
            TryComp(oldController, out CMUCargoVehicleControllerComponent? oldControllerComp))
        {
            oldControllerComp.LinkedVehicle = null;
            Dirty(oldController, oldControllerComp);
        }

        controller.Comp.LinkedVehicle = vehicle.Owner;
        vehicle.Comp.Controller = controller.Owner;
        Dirty(controller);
        Dirty(vehicle);
        _popup.PopupEntity(
            Loc.GetString("cmu-cargo-vehicle-pair-success", ("vehicle", vehicle.Owner)),
            vehicle.Owner,
            user);
    }

    private void UnlinkController(Entity<CMUCargoVehicleComponent> vehicle)
    {
        if (vehicle.Comp.Controller is { } controller &&
            TryComp(controller, out CMUCargoVehicleControllerComponent? controllerComp) &&
            controllerComp.LinkedVehicle == vehicle.Owner)
        {
            controllerComp.LinkedVehicle = null;
            Dirty(controller, controllerComp);
        }

        vehicle.Comp.Controller = null;
    }

    private void UnlinkVehicle(Entity<CMUCargoVehicleControllerComponent> controller)
    {
        if (controller.Comp.LinkedVehicle is { } vehicle &&
            TryComp(vehicle, out CMUCargoVehicleComponent? vehicleComp) &&
            vehicleComp.Controller == controller.Owner)
        {
            vehicleComp.Controller = null;
            Dirty(vehicle, vehicleComp);
        }

        controller.Comp.LinkedVehicle = null;
    }

    private void Popup(EntityUid user, string locId)
    {
        _popup.PopupEntity(Loc.GetString(locId), user, user, PopupType.SmallCaution);
    }
}
