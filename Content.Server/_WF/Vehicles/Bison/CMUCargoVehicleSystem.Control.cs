using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Bison;
using Content.Shared._WF.Vehicles.Falling;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.SSDIndicator;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.Vehicles.Bison;

/// <summary>Remote control: starting a session from the controller and handing the mind back.</summary>
public sealed partial class CMUCargoVehicleSystem
{
    /// <summary>A fall to the level below is not a jump: the link holds, and the carrier's new map is the one control is kept to.</summary>
    private void OnVehicleFell(Entity<CMUCargoVehicleControlSessionComponent> ent, ref VehicleFellEvent args)
    {
        ent.Comp.Map = Transform(ent.Owner).MapID;
    }

    private void OnReturnAction(Entity<CMUCargoVehicleControlSessionComponent> ent, ref CMUCargoVehicleReturnActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner)
            return;

        args.Handled = true;
        EndControl(ent, Loc.GetString("cmu-cargo-vehicle-control-ended-manual"));
    }

    private void OnSelfDestructAction(
        Entity<CMUCargoVehicleControlSessionComponent> ent,
        ref CMUCargoVehicleSelfDestructActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner ||
            !TryComp(ent.Owner, out CMUCargoVehicleComponent? vehicle))
        {
            return;
        }

        args.Handled = true;

        // The pilot's player is attached to the carrier, so a popup for the pilot goes to the carrier; the body is only the
        // trigger's user.
        Arm((ent.Owner, vehicle), CMUCargoVehicleArmingMode.Manual, ent.Comp.Operator, ent.Owner);
    }

    private void OnToggleBayAction(
        Entity<CMUCargoVehicleControlSessionComponent> ent,
        ref CMUCargoVehicleToggleBayActionEvent args)
    {
        if (args.Handled || args.Performer != ent.Owner ||
            !TryComp(ent.Owner, out CMUCargoVehicleComponent? vehicle))
        {
            return;
        }

        args.Handled = true;
        ToggleBay((ent.Owner, vehicle), ent.Owner);
    }

    private void OnVehiclePlayerDetached(Entity<CMUCargoVehicleControlSessionComponent> ent, ref PlayerDetachedEvent args)
    {
        EndControl(ent, Loc.GetString("cmu-cargo-vehicle-control-ended-disconnected"));
    }

    private void OnOperatorMobStateChanged(
        Entity<CMUCargoVehicleRemotePilotComponent> ent,
        ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Alive)
            return;

        EndControlForOperator(ent.Owner, Loc.GetString("cmu-cargo-vehicle-control-ended-operator-disabled"));
    }

    private void OnRemotePilotTerminating(Entity<CMUCargoVehicleRemotePilotComponent> ent, ref EntityTerminatingEvent args)
    {
        EndControlForOperator(ent.Owner, Loc.GetString("cmu-cargo-vehicle-control-ended-operator-disabled"));
    }

    private void OnRemotePilotShutdown(Entity<CMUCargoVehicleRemotePilotComponent> ent, ref ComponentShutdown args)
    {
        RestoreOperatorSsdIndicator(ent);
    }

    private void OnRemotePilotPlayerAttached(Entity<CMUCargoVehicleRemotePilotComponent> ent, ref PlayerAttachedEvent args)
    {
        SuppressSsdIndicator(ent.Owner);
    }

    private void OnRemotePilotPlayerDetached(Entity<CMUCargoVehicleRemotePilotComponent> ent, ref PlayerDetachedEvent args)
    {
        SuppressSsdIndicator(ent.Owner);
    }

    private void StartControl(Entity<CMUCargoVehicleControllerComponent> controller, EntityUid user)
    {
        if (!_hands.IsHolding(user, controller.Owner))
        {
            Popup(user, "cmu-cargo-vehicle-controller-must-hold");
            return;
        }

        if (controller.Comp.LinkedVehicle is not { } vehicleUid ||
            !TryComp(vehicleUid, out CMUCargoVehicleComponent? vehicle) ||
            !TryComp(vehicleUid, out RMCVehicleComponent? vehicleBase))
        {
            Popup(user, "cmu-cargo-vehicle-controller-unpaired");
            return;
        }

        if (vehicle.ArmingMode != CMUCargoVehicleArmingMode.None ||
            HasComp<CMUCargoVehicleControlSessionComponent>(vehicleUid))
        {
            Popup(user, "cmu-cargo-vehicle-control-busy");
            return;
        }

        if (!_mobState.IsAlive(user) ||
            !_mind.TryGetMind(user, out var mindId, out var mind) ||
            mind.OwnedEntity != user ||
            mind.VisitingEntity != null ||
            !_vehicles.TrySetOperator((vehicleUid, vehicleBase), vehicleUid))
        {
            Popup(user, "cmu-cargo-vehicle-control-unavailable");
            return;
        }

        StopMotion(user);

        var pilot = EnsureComp<CMUCargoVehicleRemotePilotComponent>(user);
        pilot.Vehicle = vehicleUid;
        pilot.Controller = controller.Owner;
        pilot.MindId = mindId;
        pilot.Map = Transform(user).MapID;
        RemoveOperatorSsdIndicator((user, pilot));
        Dirty(user, pilot);

        var session = EnsureComp<CMUCargoVehicleControlSessionComponent>(vehicleUid);
        session.Operator = user;
        session.Controller = controller.Owner;
        session.MindId = mindId;
        session.Map = Transform(vehicleUid).MapID;
        session.ReturnAction = _actions.AddAction(vehicleUid, ReturnActionId);
        session.ToggleBayAction = _actions.AddAction(vehicleUid, ToggleBayActionId);
        session.SelfDestructAction = _actions.AddAction(vehicleUid, SelfDestructActionId);
        Dirty(vehicleUid, session);

        _mind.Visit(mindId, vehicleUid, mind);

        // Visit refuses (and logs) when the mind or the carrier is already taken.
        if (mind.VisitingEntity != vehicleUid)
        {
            EndControl((vehicleUid, session), Loc.GetString("cmu-cargo-vehicle-control-ended-link-lost"));
            return;
        }

        _popup.PopupEntity(
            Loc.GetString("cmu-cargo-vehicle-control-start", ("vehicle", vehicleUid)),
            vehicleUid,
            vehicleUid);
    }

    /// <summary>Whether the link between a pilot, their controller and the carrier still holds.</summary>
    private bool ValidateSession(Entity<CMUCargoVehicleControlSessionComponent> session, out string reason)
    {
        reason = Loc.GetString("cmu-cargo-vehicle-control-ended-link-lost");
        if (TerminatingOrDeleted(session.Owner) ||
            TerminatingOrDeleted(session.Comp.Operator) ||
            TerminatingOrDeleted(session.Comp.Controller))
        {
            return false;
        }

        if (!_hands.IsHolding(session.Comp.Operator, session.Comp.Controller))
        {
            reason = Loc.GetString("cmu-cargo-vehicle-control-ended-controller-dropped");
            return false;
        }

        if (!_mobState.IsAlive(session.Comp.Operator))
        {
            reason = Loc.GetString("cmu-cargo-vehicle-control-ended-operator-disabled");
            return false;
        }

        if (!TryComp(session.Comp.Operator, out CMUCargoVehicleRemotePilotComponent? pilot))
            return false;

        // A jump takes a ship, and what is aboard, to another map; the link does not survive it on either side.
        if (Transform(session.Owner).MapID != session.Comp.Map || Transform(session.Comp.Operator).MapID != pilot.Map)
        {
            reason = Loc.GetString("cmu-cargo-vehicle-control-ended-map-changed");
            return false;
        }

        if (!TryComp(session.Comp.MindId, out MindComponent? mind) || mind.VisitingEntity != session.Owner)
            return false;

        return true;
    }

    /// <summary>Ends remote control: the pilot's mind goes back to their body and the visit's actions are taken away.</summary>
    private void EndControl(Entity<CMUCargoVehicleControlSessionComponent> session, string reason)
    {
        if (session.Comp.Ending || session.Comp.LifeStage >= ComponentLifeStage.Stopping)
            return;

        session.Comp.Ending = true;

        RemoveAction(session.Owner, ref session.Comp.ReturnAction);
        RemoveAction(session.Owner, ref session.Comp.ToggleBayAction);
        RemoveAction(session.Owner, ref session.Comp.SelfDestructAction);

        if (TryComp(session.Comp.MindId, out MindComponent? mind) && mind.VisitingEntity == session.Owner)
            _mind.UnVisit(session.Comp.MindId, mind);

        if (!TerminatingOrDeleted(session.Comp.Operator))
        {
            RemCompDeferred<CMUCargoVehicleRemotePilotComponent>(session.Comp.Operator);
            _popup.PopupEntity(reason, session.Comp.Operator, session.Comp.Operator, PopupType.SmallCaution);
        }

        StopMotion(session.Owner);
        RemCompDeferred<CMUCargoVehicleControlSessionComponent>(session.Owner);
    }

    private bool TryGetSessionForController(
        EntityUid controller,
        out Entity<CMUCargoVehicleControlSessionComponent> session)
    {
        var query = EntityQueryEnumerator<CMUCargoVehicleControlSessionComponent>();
        while (query.MoveNext(out var vehicle, out var control))
        {
            if (control.Controller != controller)
                continue;

            session = (vehicle, control);
            return true;
        }

        session = default;
        return false;
    }

    private void EndControlForController(EntityUid controller, string reason)
    {
        if (TryGetSessionForController(controller, out var session))
            EndControl(session, reason);
    }

    private void EndControlForOperator(EntityUid user, string reason)
    {
        if (!TryComp(user, out CMUCargoVehicleRemotePilotComponent? pilot) ||
            !TryComp(pilot.Vehicle, out CMUCargoVehicleControlSessionComponent? session))
        {
            return;
        }

        EndControl((pilot.Vehicle, session), reason);
    }

    /// <summary>Takes a granted action back and deletes it; the system owns it, so it is not marked temporary.</summary>
    private void RemoveAction(EntityUid owner, ref EntityUid? action)
    {
        if (action is not { } actionUid)
            return;

        action = null;
        if (TerminatingOrDeleted(actionUid))
            return;

        if (!TerminatingOrDeleted(owner))
            _actions.RemoveAction(owner, actionUid);

        QueueDel(actionUid);
    }

    private void StopMotion(EntityUid uid)
    {
        if (!TryComp(uid, out PhysicsComponent? physics))
            return;

        _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
        _physics.SetAngularVelocity(uid, 0f, body: physics);
        _physics.SetBodyStatus(uid, physics, BodyStatus.OnGround);
    }

    private void RemoveOperatorSsdIndicator(Entity<CMUCargoVehicleRemotePilotComponent> ent)
    {
        if (!TryComp<SSDIndicatorComponent>(ent.Owner, out var ssd))
        {
            ent.Comp.HadSsdIndicator = false;
            return;
        }

        ent.Comp.HadSsdIndicator = true;
        ent.Comp.SsdIndicatorIcon = ssd.Icon;
        RemComp<SSDIndicatorComponent>(ent.Owner);
    }

    /// <summary>Gives the SSD indicator back, showing the body as asleep only if its player did not come back with the mind (a disconnect).</summary>
    private void RestoreOperatorSsdIndicator(Entity<CMUCargoVehicleRemotePilotComponent> ent)
    {
        if (!ent.Comp.HadSsdIndicator || TerminatingOrDeleted(ent.Owner))
            return;

        var ssd = EnsureComp<SSDIndicatorComponent>(ent.Owner);
        ssd.Icon = ent.Comp.SsdIndicatorIcon;
        ssd.IsSSD = !HasComp<ActorComponent>(ent.Owner);
        Dirty(ent.Owner, ssd);
    }

    /// <summary>The pilot is at their post, not asleep: their body does not show the SSD icon while they drive.</summary>
    private void SuppressSsdIndicator(EntityUid uid)
    {
        if (!TryComp<SSDIndicatorComponent>(uid, out var ssd))
            return;

        ssd.IsSSD = false;
        Dirty(uid, ssd);
    }
}
