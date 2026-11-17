using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;

namespace Content.Server.NPC.Systems;

/// <summary>Lets a crewman's ship forgive an attacker before upstream retaliation remembers him.</summary>
public sealed partial class NPCRetaliationSystem
{
    [Dependency] private WGCrewSystem _wgCrew = default!;

    /// <summary>Whether this NPC is a crewman whose ship lets the attacker off.</summary>
    private bool WgForgiven(EntityUid uid, EntityUid attacker)
    {
        return HasComp<WGCrewComponent>(uid) && _wgCrew.Forgives(uid, attacker);
    }
}
