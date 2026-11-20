using Content.Shared._WG.Vehicles.Base;
using Content.Shared._WG.Vehicles.Grid;
using Content.Shared._WG.Warheads;
using Content.Shared.DragDrop;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._WG.Vehicles.Bison;

/// <summary>The parts of the Bison that both sides see: an armed carrier stops running, and a hard hit makes its own sound.</summary>
public sealed partial class SharedCMUCargoVehicleSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CMUCargoVehicleComponent, VehicleCanRunEvent>(OnVehicleCanRun);
        SubscribeLocalEvent<CMUCargoVehicleComponent, VehicleCollisionEvent>(OnVehicleCollision);
        SubscribeLocalEvent<CMUCargoVehicleComponent, CanDropTargetEvent>(OnCanDropTarget);
        SubscribeLocalEvent<WGWarheadComponent, CanDragEvent>(OnWarheadCanDrag);
    }

    /// <summary>A warhead can be dragged with the mouse, so it can be dropped onto a carrier.</summary>
    private void OnWarheadCanDrag(Entity<WGWarheadComponent> ent, ref CanDragEvent args)
    {
        args.Handled = true;
    }

    /// <summary>A loose warhead can be dropped onto a carrier with an empty bed to load it; the server checks the rest.</summary>
    private void OnCanDropTarget(Entity<CMUCargoVehicleComponent> ent, ref CanDropTargetEvent args)
    {
        if (!ent.Comp.CarriesWarheads ||
            ent.Comp.ArmingMode != CMUCargoVehicleArmingMode.None ||
            !HasComp<WGWarheadComponent>(args.Dragged) ||
            Transform(args.Dragged).Anchored)
        {
            return;
        }

        args.CanDrop = true;
        args.Handled = true;
    }

    private void OnVehicleCanRun(Entity<CMUCargoVehicleComponent> ent, ref VehicleCanRunEvent args)
    {
        if (ent.Comp.ArmingMode == CMUCargoVehicleArmingMode.Automatic)
            args.CanRun = false;
    }

    /// <summary>The mover raises the event on the server only, once for a hit and once for a blocked drive step.</summary>
    private void OnVehicleCollision(Entity<CMUCargoVehicleComponent> ent, ref VehicleCollisionEvent args)
    {
        if (ent.Comp.CollisionSound == null || MathF.Abs(args.Speed) < ent.Comp.CollisionSoundMinSpeed)
            return;

        var now = _timing.CurTime;
        if (ent.Comp.NextCollisionSound > now)
            return;

        _audio.PlayPvs(ent.Comp.CollisionSound, ent.Owner);
        ent.Comp.NextCollisionSound = now + TimeSpan.FromSeconds(ent.Comp.CollisionSoundCooldown);
    }
}
