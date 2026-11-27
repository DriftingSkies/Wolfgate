using Content.Shared.Doors;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;

namespace Content.Shared._WG.AdminVessels;

/// <summary>Queues the close of a <see cref="WGAutoCloseDoorComponent"/> door each time it opens.</summary>
public sealed class WGAutoCloseDoorSystem : EntitySystem
{
    [Dependency] private SharedDoorSystem _doors = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGAutoCloseDoorComponent, DoorStateChangedEvent>(OnStateChanged);
    }

    private void OnStateChanged(Entity<WGAutoCloseDoorComponent> ent, ref DoorStateChangedEvent args)
    {
        if (args.State == DoorState.Open)
            _doors.SetNextStateChange(ent, ent.Comp.Delay);
    }
}
