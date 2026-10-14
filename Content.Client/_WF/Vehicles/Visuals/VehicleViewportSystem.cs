using Content.Shared._WF.Vehicles.Viewport;
using Content.Shared.Movement.Events;
using Robust.Shared.GameObjects;

namespace Content.Client._WF.Vehicles.Visuals;

/// <summary>Hands the eye back as soon as someone peeking through a viewport tries to walk.</summary>
public sealed partial class VehicleViewportSystem : EntitySystem
{
    [Dependency] private SharedEyeSystem _eye = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleViewportUserComponent, MoveInputEvent>(OnUserMove);
    }

    private void OnUserMove(Entity<VehicleViewportUserComponent> ent, ref MoveInputEvent args)
    {
        if (!args.HasDirectionalMovement)
            return;

        if (TryComp(ent, out EyeComponent? eye))
            _eye.SetTarget(ent.Owner, ent.Comp.PreviousTarget, eye);
    }
}
