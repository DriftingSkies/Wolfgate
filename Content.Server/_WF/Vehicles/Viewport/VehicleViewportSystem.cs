using System;
using System.Collections.Generic;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Core;
using Content.Shared._WF.Vehicles.Viewport;
using Content.Shared.Interaction;
using Content.Shared.Movement.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Viewport;

/// <summary>
/// Lets people look out through an interior viewport, or into an interior from a doorway. A view lasts until the viewer moves,
/// uses it again, or stops being where it needs them: inside the vehicle for a viewport, at the doorway for a peek.
/// </summary>
public sealed partial class VehicleViewportSystem : EntitySystem
{
    private static readonly EntProtoId VehiclePeekAnchor = "VehiclePeekAnchor";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private VehicleInteriorSystem _vehicles = default!;
    [Dependency] private VehicleViewToggleSystem _viewToggle = default!;

    private readonly List<EntityUid> _stale = new();
    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleViewportComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<VehicleViewportComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<VehicleViewportComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<VehicleEnterComponent, GetVerbsEvent<AlternativeVerb>>(OnVehicleEnterVerbs);
        SubscribeLocalEvent<VehicleViewportUserComponent, MoveInputEvent>(OnUserMove);
        SubscribeLocalEvent<VehicleViewportUserComponent, ComponentShutdown>(OnUserShutdown);
    }

    private void OnUserShutdown(Entity<VehicleViewportUserComponent> ent, ref ComponentShutdown args)
    {
        // A viewer who is deleted never gets to close the view, and the anchor would stay on the interior map.
        if (ent.Comp.PeekTarget is { } peekTarget && !TerminatingOrDeleted(peekTarget))
            QueueDel(peekTarget);

        if (ent.Comp.Source is { } source)
            _viewToggle.DisableViewToggle(ent.Owner, source);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        _stale.Clear();
        var query = EntityQueryEnumerator<VehicleViewportUserComponent>();
        while (query.MoveNext(out var uid, out var state))
        {
            if (!IsViewValid(uid, state))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            CloseViewport(uid);
        }

        _stale.Clear();
    }

    /// <summary>A viewport view needs its viewer on the viewport's map, a peek needs them at a doorway of the vehicle.</summary>
    private bool IsViewValid(EntityUid user, VehicleViewportUserComponent state)
    {
        if (state.Source is not { } source || TerminatingOrDeleted(source))
            return false;

        if (state.PeekTarget != null)
            return _vehicles.TryFindEntryPoint(source, user, out _);

        return Transform(user).MapID == Transform(source).MapID;
    }

    private void OnActivate(Entity<VehicleViewportComponent> ent, ref ActivateInWorldEvent args)
    {
        if (_net.IsClient || args.Handled)
            return;

        if (!_vehicles.TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle is null)
            return;

        if (ToggleViewport(args.User, vehicle.Value, ent.Owner))
            args.Handled = true;
    }

    private void OnUserMove(Entity<VehicleViewportUserComponent> ent, ref MoveInputEvent args)
    {
        if (_net.IsClient || !args.HasDirectionalMovement)
            return;

        CloseViewport(ent.Owner, ent.Comp);
    }

    private void OnInteractUsing(Entity<VehicleViewportComponent> ent, ref InteractUsingEvent args)
    {
        if (_net.IsClient || args.Handled)
            return;

        if (!_vehicles.TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle is null)
            return;

        if (ToggleViewport(args.User, vehicle.Value, ent.Owner))
            args.Handled = true;
    }

    private void OnInteractHand(Entity<VehicleViewportComponent> ent, ref InteractHandEvent args)
    {
        if (_net.IsClient || args.Handled)
            return;

        if (!_vehicles.TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle is null)
            return;

        if (ToggleViewport(args.User, vehicle.Value, ent.Owner))
            args.Handled = true;
    }

    private void OnVehicleEnterVerbs(Entity<VehicleEnterComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (_net.IsClient ||
            !args.CanInteract ||
            !args.CanAccess ||
            args.Using != null ||
            !_vehicles.TryFindEntryPoint(ent.Owner, args.User, out var entryIndex))
        {
            return;
        }

        var user = args.User;
        var vehicle = ent.Owner;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("rmc-vehicle-look-inside"),
            Act = () => ToggleInteriorPeek(user, vehicle, entryIndex),
        });
    }

    private bool ToggleViewport(EntityUid user, EntityUid vehicle, EntityUid source)
    {
        if (TryComp(user, out VehicleViewportUserComponent? existing))
        {
            CloseViewport(user, existing);
            return true;
        }

        // The driver's view toggle is already set for the seat; a viewport would overwrite its targets with the vehicle itself.
        if (HasComp<VehicleViewToggleComponent>(user))
        {
            _popup.PopupEntity(Loc.GetString("wf-vehicle-viewport-driving"), user, user);
            return true;
        }

        var userState = EnsureComp<VehicleViewportUserComponent>(user);
        if (TryComp(user, out EyeComponent? newEye))
            userState.PreviousTarget = newEye.Target;
        userState.Source = source;

        var watching = EnsureComp<VehicleWatchingComponent>(user);
        watching.Watching = vehicle;
        Dirty(user, watching);

        _eye.SetTarget(user, vehicle);
        _viewToggle.EnableViewToggle(user, vehicle, source, userState.PreviousTarget, isOutside: true);
        return true;
    }

    private bool ToggleInteriorPeek(EntityUid user, EntityUid vehicle, int entryIndex)
    {
        if (TryComp(user, out VehicleViewportUserComponent? existing))
        {
            if (existing.Source == vehicle)
            {
                CloseViewport(user, existing);
                return true;
            }

            CloseViewport(user, existing);
        }

        if (!_vehicles.TryGetInteriorEntryCoordinates(vehicle, entryIndex, out var peekCoords))
            return false;

        var userState = EnsureComp<VehicleViewportUserComponent>(user);
        if (TryComp(user, out EyeComponent? eye))
            userState.PreviousTarget = eye.Target;
        userState.Source = vehicle;
        userState.PeekTarget = Spawn(VehiclePeekAnchor, peekCoords);

        var watching = EnsureComp<VehicleWatchingComponent>(user);
        watching.Watching = vehicle;
        Dirty(user, watching);

        _eye.SetTarget(user, userState.PeekTarget);
        Dirty(user, userState);
        return true;
    }

    private void CloseViewport(EntityUid user, VehicleViewportUserComponent? state = null)
    {
        state ??= CompOrNull<VehicleViewportUserComponent>(user);
        if (state == null)
            return;

        if (TryComp(user, out EyeComponent? eye))
            _eye.SetTarget(user, state.PreviousTarget, eye);

        if (state.Source != null)
            _viewToggle.DisableViewToggle(user, state.Source.Value);

        if (state.PeekTarget is { } peekTarget && Exists(peekTarget))
            QueueDel(peekTarget);

        RemCompDeferred<VehicleViewportUserComponent>(user);
        RemCompDeferred<VehicleWatchingComponent>(user);
    }
}
