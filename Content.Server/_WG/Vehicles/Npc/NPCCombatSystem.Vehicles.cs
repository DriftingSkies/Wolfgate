using Content.Server._WG.Vehicles.Npc;

namespace Content.Server.NPC.Systems;

public sealed partial class NPCCombatSystem
{
    /// <summary>An occupied vehicle is within reach once its hull is, however far its middle is.</summary>
    private bool WgHullInReach(EntityUid attacker, EntityUid target, float range)
    {
        return HasComp<WGOccupiedVehicleComponent>(target) && _interaction.InRangeUnobstructed(attacker, target, range);
    }
}
