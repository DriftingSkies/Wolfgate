using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;

namespace Content.Server.NPC.Systems;

/// <summary>Lets access-aware NPCs use their credentials before falling back to existing obstacle handling.</summary>
public sealed partial class NPCSteeringSystem
{
    [Dependency] private AccessReaderSystem _crewAccessReader = default!;
    [Dependency] private SharedDoorSystem _crewDoors = default!;

    /// <summary>Checks both bodies' hard fixtures without the engine helper's duplicated first-body lookup.</summary>
    private bool IsCrewObstacleCollidable(EntityUid uid, EntityUid obstacle)
    {
        if (!_physicsQuery.TryGetComponent(uid, out var body) || !body.CanCollide || !body.Hard ||
            !_physicsQuery.TryGetComponent(obstacle, out var other) || !other.CanCollide || !other.Hard)
            return false;

        var (layer, mask) = _physics.GetHardCollision(uid);
        var (otherLayer, otherMask) = _physics.GetHardCollision(obstacle);
        return (layer & otherMask) != 0 || (otherLayer & mask) != 0;
    }

    /// <summary>Opens a permitted door normally, respecting power, bolts, and interaction range.</summary>
    private bool TryOpenCrewAccessDoor(EntityUid uid, NPCSteeringComponent steering, List<EntityUid> obstacles)
    {
        if ((steering.Flags & (PathFlags.Access | PathFlags.Interact)) != (PathFlags.Access | PathFlags.Interact))
            return false;

        foreach (var obstacle in obstacles)
        {
            if (!TryComp<DoorComponent>(obstacle, out var door) || door.State == DoorState.Open)
                continue;
            if (door.State == DoorState.Opening)
                return true;
            if (!_crewAccessReader.IsAllowed(uid, obstacle) || !_crewDoors.CanOpen(obstacle, door, uid))
                continue;

            _interaction.InteractionActivate(uid, obstacle);
            if (door.State is DoorState.Opening or DoorState.Open)
                return true;
        }
        return false;
    }
}
