using Content.Shared.Doors;
using Content.Shared.Doors.Components;

namespace Content.Shared._WF.AdminVessels;

/// <summary>Holds an interlocked door shut while its partner is anything but closed.</summary>
public sealed class WFDoorInterlockSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFDoorInterlockComponent, BeforeDoorOpenedEvent>(OnBeforeOpen);
    }

    private void OnBeforeOpen(Entity<WFDoorInterlockComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (ent.Comp.Partner is { } partner && TryComp<DoorComponent>(partner, out var door) && door.State != DoorState.Closed)
            args.Cancel();
    }
}
