using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.Planets.Parachute;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Vehicles.Falling;

/// <summary>Temporarily gives parachuted vehicles the existing canopy-controlled z-level physics.</summary>
public sealed class WGVehicleParachuteSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleFallComponent, WGParachuteAttachAttemptEvent>(OnAttachAttempt);
        SubscribeLocalEvent<WGParachutedComponent, ComponentStartup>(OnAttached);
        SubscribeLocalEvent<WGParachutedComponent, ComponentRemove>(OnRemoved);
    }

    private void OnAttachAttempt(Entity<VehicleFallComponent> ent, ref WGParachuteAttachAttemptEvent args)
    {
        args.Allowed |= ent.Comp.Enabled;
    }

    private void OnAttached(Entity<WGParachutedComponent> ent, ref ComponentStartup args)
    {
        if (!HasComp<VehicleFallComponent>(ent.Owner) || HasComp<CEZPhysicsComponent>(ent.Owner))
            return;

        EnsureComp<WGVehicleParachutePhysicsComponent>(ent.Owner);
        var physics = EnsureComp<CEZPhysicsComponent>(ent.Owner);
        physics.AutoStep = false;
        Dirty(ent.Owner, physics);
    }

    private void OnRemoved(Entity<WGParachutedComponent> ent, ref ComponentRemove args)
    {
        if (!HasComp<WGVehicleParachutePhysicsComponent>(ent.Owner) || TerminatingOrDeleted(ent.Owner))
            return;

        RemCompDeferred<CEZPhysicsComponent>(ent.Owner);
        RemCompDeferred<WGVehicleParachutePhysicsComponent>(ent.Owner);
    }
}

/// <summary>Marks z-physics added only for a vehicle's parachute, so removal restores its ordinary falling path.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGVehicleParachutePhysicsComponent : Component;
