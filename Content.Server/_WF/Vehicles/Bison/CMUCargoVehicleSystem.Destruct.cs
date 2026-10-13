using System.Numerics;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Bison;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.Vehicles.Bison;

/// <summary>The self-destruct: arming, the countdown and the wreck.</summary>
public sealed partial class CMUCargoVehicleSystem
{
    /// <summary>Arms the self-destruct for good; an automatic arming also ends control and stops the carrier.</summary>
    private void Arm(
        Entity<CMUCargoVehicleComponent> vehicle,
        CMUCargoVehicleArmingMode mode,
        EntityUid? user,
        EntityUid? popupTo = null)
    {
        if (vehicle.Comp.ArmingMode != CMUCargoVehicleArmingMode.None)
        {
            if ((popupTo ?? user) is { } recipient)
                Popup(recipient, "cmu-cargo-vehicle-already-armed");
            return;
        }

        vehicle.Comp.ArmingMode = mode;
        Dirty(vehicle);

        if (mode == CMUCargoVehicleArmingMode.Automatic)
        {
            if (TryComp(vehicle.Owner, out CMUCargoVehicleControlSessionComponent? session))
                EndControl((vehicle.Owner, session), Loc.GetString("cmu-cargo-vehicle-control-ended-critical-damage"));

            StopMotion(vehicle.Owner);
            if (TryComp(vehicle.Owner, out RMCVehicleComponent? vehicleComp))
                _vehicles.RefreshCanRun((vehicle.Owner, vehicleComp));
        }

        _trigger.HandleTimerTrigger(
            vehicle.Owner,
            user,
            vehicle.Comp.DetonationDelay,
            vehicle.Comp.BeepInterval,
            0f,
            vehicle.Comp.BeepSound);

        _popup.PopupEntity(
            Loc.GetString(mode == CMUCargoVehicleArmingMode.Automatic
                ? "cmu-cargo-vehicle-auto-armed"
                : "cmu-cargo-vehicle-manual-armed"),
            vehicle.Owner,
            Filter.Pvs(vehicle.Owner),
            true,
            PopupType.LargeCaution);
    }

    /// <summary>The countdown ran out: sends the pilot back, throws the cargo clear and leaves a wreck, debris and fire; the blast is the carrier's own ExplodeOnTrigger.</summary>
    private void OnVehicleTriggered(Entity<CMUCargoVehicleComponent> ent, ref TriggerEvent args)
    {
        if (TryComp(ent.Owner, out CMUCargoVehicleControlSessionComponent? session))
            EndControl((ent.Owner, session), Loc.GetString("cmu-cargo-vehicle-control-ended-vehicle-lost"));

        ScatterCargo(ent);

        var xform = Transform(ent.Owner);
        var coords = xform.Coordinates;
        var wreck = Spawn(ent.Comp.WreckPrototype, coords);
        _transform.SetLocalRotation(wreck, xform.LocalRotation);

        if (ent.Comp.DebrisPrototypes.Count > 0)
        {
            var debrisCount = _random.Next(ent.Comp.MinimumDebris, ent.Comp.MaximumDebris + 1);
            var firstDebris = _random.Next(ent.Comp.DebrisPrototypes.Count);
            for (var i = 0; i < debrisCount; i++)
            {
                var debrisPrototype = ent.Comp.DebrisPrototypes[(firstDebris + i) % ent.Comp.DebrisPrototypes.Count];
                var debris = Spawn(debrisPrototype, coords);
                _throwing.TryThrow(
                    debris,
                    _random.NextAngle().ToWorldVec(),
                    baseThrowSpeed: _random.NextFloat(4f, 7f),
                    doSpin: true,
                    compensateFriction: true);
            }
        }

        SpawnFireDiamond(ent.Comp, coords);
    }

    /// <summary>Sets every tile within <see cref="CMUCargoVehicleComponent.FireRange"/> steps of the carrier alight; tiles that cannot burn are skipped.</summary>
    private void SpawnFireDiamond(CMUCargoVehicleComponent vehicle, EntityCoordinates coords)
    {
        for (var x = -vehicle.FireRange; x <= vehicle.FireRange; x++)
        {
            for (var y = -vehicle.FireRange; y <= vehicle.FireRange; y++)
            {
                if (Math.Abs(x) + Math.Abs(y) > vehicle.FireRange)
                    continue;

                _tileFire.TryDoTileFire(coords.Offset(new Vector2(x, y)), stage: vehicle.FireStage);
            }
        }
    }
}
