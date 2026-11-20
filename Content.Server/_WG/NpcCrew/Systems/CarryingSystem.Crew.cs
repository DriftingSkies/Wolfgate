namespace Content.Server.Carrying;

public sealed partial class CarryingSystem
{
    /// <summary>
    /// Picks someone up at once, for an NPC medic taking in a patient: no do-after, and no rule that the carrier
    /// weighs twice the carried, which no crewman meets with a grown patient.
    /// </summary>
    public bool TryCrewCarry(EntityUid carrier, EntityUid carried)
    {
        if (HasComp<CarryingComponent>(carrier) || !CanCarry(carrier, carried))
            return false;

        Carry(carrier, carried);
        return true;
    }
}
