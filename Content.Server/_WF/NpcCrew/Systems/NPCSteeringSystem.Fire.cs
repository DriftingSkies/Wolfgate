using Content.Server._WF.NpcCrew.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NPC.Components;
using Content.Shared._ES.TileFires;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.NPC.Systems;

/// <summary>NPC crew stop short of a burning tile instead of walking through it, unless they are fighting.</summary>
public sealed partial class NPCSteeringSystem
{
    [Dependency] private AtmosphereSystem _crewAtmos = default!;

    private readonly HashSet<Entity<ESTileFireComponent>> _crewFires = new();

    /// <summary>Whether a crewman's next step is onto a tile on fire, by a tile fire or burning gas.</summary>
    private bool IsCrewFireAhead(EntityUid uid, EntityCoordinates target)
    {
        if (!HasComp<WFCrewComponent>(uid) || HasComp<NPCMeleeCombatComponent>(uid) || HasComp<NPCRangedCombatComponent>(uid))
            return false;

        if (_transform.GetGrid(target) is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var tile = _mapSystem.TileIndicesFor(gridUid, grid, target);
        if (_crewAtmos.IsHotspotActive(gridUid, tile))
            return true;

        _crewFires.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, tile, _crewFires, -0.1f);
        return _crewFires.Count > 0;
    }
}
