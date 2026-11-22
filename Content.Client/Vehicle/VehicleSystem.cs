using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Client.Vehicle;

public sealed partial class VehicleSystem : SharedVehicleSystem
{
    [Dependency] private EyeSystem _eye = default!;

    public override void Initialize()
    {
        base.Initialize();

        // WOLFGATE(Vehicles) START: the shared buckle handlers set the rider's view; client-only changes leaked into the
        // implicit server state of a body spawned riding, so the view snapped back to the vehicle after leaving it
        // SubscribeLocalEvent<RiderComponent, ComponentStartup>(OnRiderStartup);
        // SubscribeLocalEvent<RiderComponent, ComponentShutdown>(OnRiderShutdown);
        // WOLFGATE END
        SubscribeLocalEvent<RiderComponent, ComponentHandleState>(OnRiderHandleState);
        SubscribeLocalEvent<VehicleComponent, AppearanceChangeEvent>(OnVehicleAppearanceChange);
    }

    private void OnRiderStartup(EntityUid uid, RiderComponent component, ComponentStartup args)
    {
        // Center the player's eye on the vehicle
        if (TryComp(uid, out EyeComponent? eyeComp))
        {
            _eye.SetTarget(uid, eyeComp.Target ?? component.Vehicle, eyeComp);
        }
    }

    private void OnRiderShutdown(EntityUid uid, RiderComponent component, ComponentShutdown args)
    {
        // reset the riders eye centering.
        if (TryComp(uid, out EyeComponent? eyeComp))
        {
            _eye.SetTarget(uid, null, eyeComp);
        }
    }

    private void OnRiderHandleState(EntityUid uid, RiderComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not RiderComponentState state)
            return;

        var entity = EnsureEntity<RiderComponent>(state.Entity, uid);

        // WOLFGATE(Vehicles) START: the shared buckle handlers set the rider's view
        // if (TryComp(uid, out EyeComponent? eyeComp) && eyeComp.Target == component.Vehicle)
        // {
        //     _eye.SetTarget(uid, entity, eyeComp);
        // }
        // WOLFGATE END

        component.Vehicle = entity;
    }

    private void OnVehicleAppearanceChange(EntityUid uid, VehicleComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (component.HideRider
            && Appearance.TryGetData<bool>(uid, VehicleVisuals.HideRider, out var hide, args.Component)
            && TryComp<SpriteComponent>(component.LastRider, out var riderSprite))
            riderSprite.Visible = !hide;

        // First check is for the sprite itself
        if (Appearance.TryGetData<int>(uid, VehicleVisuals.DrawDepth, out var drawDepth, args.Component))
            args.Sprite.DrawDepth = drawDepth;

        // Set vehicle layer to animated or not (i.e. are the wheels turning or not)
        if (component.AutoAnimate
            && Appearance.TryGetData<bool>(uid, VehicleVisuals.AutoAnimate, out var autoAnimate, args.Component))
            args.Sprite.LayerSetAutoAnimated(VehicleVisualLayers.AutoAnimate, autoAnimate);
    }
}

public enum VehicleVisualLayers : byte
{
    /// Layer for the vehicle's wheels
    AutoAnimate,
}
