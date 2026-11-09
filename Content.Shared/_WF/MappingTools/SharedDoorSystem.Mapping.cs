using Content.Shared.Doors.Components;

namespace Content.Shared.Doors.Systems;

public abstract partial class SharedDoorSystem
{
    /// <summary>
    /// Puts a door straight into its open or closed rest state with matching collision, no animation, sound or crush.
    /// For mapping edits on paused maps, where a door's timed states never finish.
    /// </summary>
    public void WFSetRestingState(Entity<DoorComponent> ent, bool open)
    {
        ent.Comp.NextStateChange = null;
        ent.Comp.Partial = false;
        SetState(ent, open ? DoorState.Open : DoorState.Closed, ent.Comp);
        SetCollidable(ent, !open, ent.Comp);
        Dirty(ent);
    }

    /// <summary>Sets a door's bolts without the power check or sound, for mapping edits on unpowered maps.</summary>
    public void WFSetBoltsQuiet(Entity<DoorBoltComponent> ent, bool value)
    {
        if (ent.Comp.BoltsDown == value)
            return;

        ent.Comp.BoltsDown = value;
        Dirty(ent);
        UpdateBoltLightStatus(ent);
        RaiseLocalEvent(ent.Owner, new DoorBoltsChangedEvent(value));
    }
}
