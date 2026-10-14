using System.Linq;
using Content.Server.Storage.Components;
using Content.Shared._WF.Vehicles.Bison;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.DoAfter;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Vehicles.Bison;

/// <summary>The cargo bay: loading, unloading and scattering the crate.</summary>
public sealed partial class CMUCargoVehicleSystem
{
    /// <summary>How close the user and a crate must be to the carrier to load or unload it.</summary>
    private const float CargoReach = 2.5f;

    private void OnVehicleGetVerbs(Entity<CMUCargoVehicleComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        // Pulling a crate puts a virtual item in the user's hand; that is not a held item.
        if (!args.CanInteract || !args.CanAccess || args.Using is { } held && !HasComp<VirtualItemComponent>(held))
            return;

        if (ent.Comp.ArmingMode != CMUCargoVehicleArmingMode.None)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(ent.Comp.BayOpen
                ? "cmu-cargo-vehicle-verb-close"
                : "cmu-cargo-vehicle-verb-open"),
            Priority = 3,
            Act = () => ToggleBay(ent, user),
        });

        if (!CanHandleCargo(ent))
            return;

        if (ent.Comp.CargoContainer?.ContainedEntity is { })
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cmu-cargo-vehicle-verb-unload"),
                Priority = 2,
                Act = () => StartUnload(ent, user),
            });
            return;
        }

        if (!TryGetPulledCrate(ent, user, out var crate))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("cmu-cargo-vehicle-verb-load"),
            Priority = 2,
            Act = () => StartLoad(ent, user, crate),
        });
    }

    private void OnLoadDoAfter(Entity<CMUCargoVehicleComponent> ent, ref CMUCargoVehicleLoadDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (args.Cancelled || args.Used is not { } crate || !CanFinishLoad(ent, args.User, crate))
            return;

        if (TryComp(crate, out PullableComponent? pullable))
            _pulling.TryStopPull(crate, pullable, args.User);

        if (!_containers.Insert(crate, ent.Comp.CargoContainer!))
            return;

        _popup.PopupEntity(
            Loc.GetString("cmu-cargo-vehicle-load-finished", ("crate", crate)),
            ent.Owner,
            args.User);
    }

    private void OnUnloadDoAfter(Entity<CMUCargoVehicleComponent> ent, ref CMUCargoVehicleUnloadDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (args.Cancelled || !CanFinishUnload(ent, args.User, out var crate))
            return;

        _containers.Remove(crate, ent.Comp.CargoContainer!);
        var offset = _random.NextAngle().ToWorldVec() * 0.8f;

        // Not the user's own coordinates: a user buckled to a chair is parented to it, and the crate would go with the chair.
        _transform.SetCoordinates(crate, _transform.GetMoverCoordinates(args.User).Offset(offset));
        _popup.PopupEntity(
            Loc.GetString("cmu-cargo-vehicle-unload-finished", ("crate", crate)),
            ent.Owner,
            args.User);
    }

    private void ToggleBay(Entity<CMUCargoVehicleComponent> vehicle, EntityUid user)
    {
        if (vehicle.Comp.ArmingMode != CMUCargoVehicleArmingMode.None)
        {
            Popup(user, "cmu-cargo-vehicle-cargo-armed");
            return;
        }

        vehicle.Comp.BayOpen = !vehicle.Comp.BayOpen;
        Dirty(vehicle);
        UpdateBayAppearance(vehicle);
        _audio.PlayPvs(vehicle.Comp.RampSound, vehicle.Owner);
        _popup.PopupEntity(
            Loc.GetString(vehicle.Comp.BayOpen
                ? "cmu-cargo-vehicle-bay-opened"
                : "cmu-cargo-vehicle-bay-closed"),
            vehicle.Owner,
            user);
    }

    private void StartLoad(Entity<CMUCargoVehicleComponent> vehicle, EntityUid user, EntityUid crate)
    {
        if (!CanFinishLoad(vehicle, user, crate))
            return;

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            vehicle.Comp.CargoDelay,
            new CMUCargoVehicleLoadDoAfterEvent(),
            vehicle.Owner,
            target: vehicle.Owner,
            used: crate)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
            DuplicateCondition = DuplicateConditions.SameEvent | DuplicateConditions.SameTarget,
            ExtraCheck = () => CanFinishLoad(vehicle, user, crate),
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("cmu-cargo-vehicle-load-start"), vehicle.Owner, user);
    }

    private void StartUnload(Entity<CMUCargoVehicleComponent> vehicle, EntityUid user)
    {
        if (!CanFinishUnload(vehicle, user, out _))
            return;

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            vehicle.Comp.CargoDelay,
            new CMUCargoVehicleUnloadDoAfterEvent(),
            vehicle.Owner,
            target: vehicle.Owner)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
            DuplicateCondition = DuplicateConditions.SameEvent | DuplicateConditions.SameTarget,
            ExtraCheck = () => CanFinishUnload(vehicle, user, out _),
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("cmu-cargo-vehicle-unload-start"), vehicle.Owner, user);
    }

    private bool CanFinishLoad(Entity<CMUCargoVehicleComponent> vehicle, EntityUid user, EntityUid crate)
    {
        return CanHandleCargo(vehicle) &&
               vehicle.Comp.CargoContainer is { ContainedEntity: null } cargoContainer &&
               !TerminatingOrDeleted(crate) &&
               IsLoadableCrate(vehicle, crate) &&
               TryComp(user, out PullerComponent? puller) &&
               puller.Pulling == crate &&
               IsNear(user, vehicle.Owner) &&
               IsNear(crate, vehicle.Owner) &&
               _containers.CanInsert(crate, cargoContainer);
    }

    private bool CanFinishUnload(
        Entity<CMUCargoVehicleComponent> vehicle,
        EntityUid user,
        out EntityUid crate)
    {
        crate = default;
        if (!CanHandleCargo(vehicle) ||
            vehicle.Comp.CargoContainer?.ContainedEntity is not { } contained ||
            !IsNear(user, vehicle.Owner))
        {
            return false;
        }

        crate = contained;
        return true;
    }

    /// <summary>The bay must be open, the self-destruct not armed and the carrier standing still.</summary>
    private bool CanHandleCargo(Entity<CMUCargoVehicleComponent> vehicle)
    {
        if (!vehicle.Comp.BayOpen || vehicle.Comp.ArmingMode != CMUCargoVehicleArmingMode.None)
            return false;

        return !TryComp(vehicle.Owner, out GridVehicleMoverComponent? mover) ||
               (!mover.IsMoving && MathF.Abs(mover.CurrentSpeed) < 0.01f);
    }

    private bool TryGetPulledCrate(Entity<CMUCargoVehicleComponent> vehicle, EntityUid user, out EntityUid crate)
    {
        crate = default;
        if (!TryComp(user, out PullerComponent? puller) || puller.Pulling is not { } pulled ||
            !IsLoadableCrate(vehicle, pulled) ||
            !IsNear(pulled, vehicle.Owner))
        {
            return false;
        }

        crate = pulled;
        return true;
    }

    /// <summary>A crate is an entity storage whose prototype is, or inherits from, one of the carrier's crate parents.</summary>
    private bool IsLoadableCrate(Entity<CMUCargoVehicleComponent> vehicle, EntityUid uid)
    {
        if (!HasComp<EntityStorageComponent>(uid) ||
            !TryComp(uid, out MetaDataComponent? meta) ||
            meta.EntityPrototype is not { } prototype)
        {
            return false;
        }

        foreach (var (id, _) in _prototypes.EnumerateAllParents<EntityPrototype>(prototype.ID, includeSelf: true))
        {
            if (vehicle.Comp.CrateParents.Contains(id))
                return true;
        }

        return false;
    }

    /// <summary>Opens the loaded crate onto the floor around the carrier, throws its contents and deletes the crate.</summary>
    private void ScatterCargo(Entity<CMUCargoVehicleComponent> vehicle)
    {
        if (vehicle.Comp.CargoContainer?.ContainedEntity is not { } crate)
            return;

        _containers.Remove(crate, vehicle.Comp.CargoContainer);
        var coords = Transform(vehicle.Owner).Coordinates;

        if (TryComp(crate, out EntityStorageComponent? storage))
        {
            foreach (var item in storage.Contents.ContainedEntities.ToArray())
            {
                _containers.Remove(item, storage.Contents);
                _transform.SetCoordinates(item, coords);
                _throwing.TryThrow(
                    item,
                    _random.NextAngle().ToWorldVec(),
                    baseThrowSpeed: _random.NextFloat(3f, 7f),
                    doSpin: true,
                    compensateFriction: true);
            }
        }

        QueueDel(crate);
    }

    private bool IsNear(EntityUid first, EntityUid second)
    {
        var firstPosition = _transform.GetMapCoordinates(first);
        var secondPosition = _transform.GetMapCoordinates(second);

        return firstPosition.MapId == secondPosition.MapId &&
               (firstPosition.Position - secondPosition.Position).Length() <= CargoReach;
    }

    private void UpdateBayAppearance(Entity<CMUCargoVehicleComponent> vehicle)
    {
        _appearance.SetData(vehicle.Owner, CMUCargoVehicleVisuals.BayOpen, vehicle.Comp.BayOpen);
    }
}
