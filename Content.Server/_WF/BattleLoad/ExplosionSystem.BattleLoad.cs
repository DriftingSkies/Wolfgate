using Content.Server._WF.BattleLoad;
using Content.Shared.Explosion;
using Content.Shared.Tiles;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;

namespace Content.Server.Explosion.EntitySystems;

/// <summary>
/// Cheaper explosions for ship fights: bullet-sized blasts hit their few tiles directly instead of going through the
/// queue, and small blasts on one grid merge from further apart.
/// </summary>
public sealed partial class ExplosionSystem
{
    [Dependency] private WFBattleHeatSystem _wfBattleHeat = default!;

    /// <summary>The epicentre tile and its four neighbours: the tiles a bullet-sized blast reaches.</summary>
    private static readonly Vector2i[] WFMicroOffsets =
    {
        new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    private readonly List<WFMicroExplosion> _wfMicroQueue = new();
    private readonly List<WFMicroExplosion> _wfMicroBatch = new();
    private readonly HashSet<EntityUid> _wfMicroProcessed = new();
    private readonly List<(Vector2i GridIndices, Tile Tile)> _wfMicroTiles = new();

    private readonly record struct WFMicroExplosion(
        EntityCoordinates Epicenter,
        ExplosionPrototype Proto,
        float TotalIntensity,
        float MaxTileIntensity,
        float TileBreakScale,
        int MaxTileBreak,
        bool CanCreateVacuum,
        EntityUid? Cause);

    /// <summary>
    /// Takes a bullet-sized explosion on a grid off the queue, to be applied at the start of the next update. No
    /// visuals, sound, shake or admin log, like any other bullet hit.
    /// </summary>
    private bool WFTryQueueMicroExplosion(EntityCoordinates epicenter,
        string typeId,
        float totalIntensity,
        float maxTileIntensity,
        float tileBreakScale,
        int maxTileBreak,
        bool canCreateVacuum,
        EntityUid? cause)
    {
        if (!_wfBattleHeat.MicroExplosions
            || totalIntensity <= 0
            || totalIntensity > _wfBattleHeat.MicroMaxTotal
            || maxTileIntensity > _wfBattleHeat.MicroMaxTile
            || !HasComp<MapGridComponent>(epicenter.EntityId)
            || !_prototypeManager.TryIndex<ExplosionPrototype>(typeId, out var type))
            return false;

        _wfBattleHeat.Record(_transformSystem.ToMapCoordinates(epicenter));
        _wfMicroQueue.Add(new WFMicroExplosion(epicenter, type, totalIntensity, maxTileIntensity, tileBreakScale,
            maxTileBreak, canCreateVacuum, cause));
        return true;
    }

    /// <summary>Applies the bullet-sized explosions queued since the last update.</summary>
    private void WFProcessMicroExplosions()
    {
        if (_wfMicroQueue.Count == 0)
            return;

        // A blast set off by one of these, a fuel tank say, waits for the next tick.
        _wfMicroBatch.AddRange(_wfMicroQueue);
        _wfMicroQueue.Clear();
        try
        {
            foreach (var micro in _wfMicroBatch)
            {
                WFApplyMicroExplosion(micro);
            }
        }
        finally
        {
            _wfMicroBatch.Clear();
        }
    }

    /// <summary>
    /// Damages the epicentre tile at up to the blast's tile intensity and spreads what is left over its four
    /// neighbours, through the same per-tile code as a full explosion.
    /// </summary>
    private void WFApplyMicroExplosion(WFMicroExplosion micro)
    {
        var gridUid = micro.Epicenter.EntityId;
        if (TerminatingOrDeleted(gridUid)
            || !TryComp<MapGridComponent>(gridUid, out var grid)
            || !TryComp<BroadphaseComponent>(gridUid, out var lookup))
            return;

        // Safe zones block explosions, as in SpawnExplosion.
        if (TryComp<ProtectedGridComponent>(gridUid, out var protectedGrid) && protectedGrid.PreventExplosions)
            return;

        var mapPos = _transformSystem.ToMapCoordinates(micro.Epicenter);
        var center = _map.LocalToTile(gridUid, grid, micro.Epicenter);
        // A shell bursting just off the hull puts its whole blast into the tiles around it.
        var centerExists = _map.TryGetTileRef(gridUid, grid, center, out var centerRef) && !centerRef.Tile.IsEmpty;
        var centerIntensity = centerExists ? MathF.Min(micro.MaxTileIntensity, micro.TotalIntensity) : 0f;
        var ringIntensity = MathF.Min(micro.MaxTileIntensity, (micro.TotalIntensity - centerIntensity) / 4f);
        var damageModifier = _damageableSystem.UniversalExplosionDamageModifier * _damageableSystem.UniversalAllDamageModifier;

        _wfMicroProcessed.Clear();
        _wfMicroTiles.Clear();
        for (var i = 0; i < WFMicroOffsets.Length; i++)
        {
            var intensity = i == 0 ? centerIntensity : ringIntensity;
            if (intensity <= 0f)
                continue;

            var tile = center + WFMicroOffsets[i];
            if (!_map.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;

            var damage = micro.Proto.DamagePerIntensity * (intensity * damageModifier);
            var throwForce = micro.Proto.ThrowEntitiesOnExplosion ? 10 * MathF.Sqrt(intensity) : 0f;
            var canDamageFloor = ExplodeTile(lookup,
                (gridUid, grid),
                tile,
                throwForce,
                damage,
                mapPos,
                _wfMicroProcessed,
                micro.Proto.ID,
                micro.Proto.FireStacks,
                micro.Proto.Temperature,
                intensity,
                micro.Cause);

            if (canDamageFloor)
                DamageFloorTile(tileRef, intensity * micro.TileBreakScale, micro.MaxTileBreak, micro.CanCreateVacuum, _wfMicroTiles, micro.Proto);
        }

        if (_wfMicroTiles.Count > 0 && !TerminatingOrDeleted(gridUid))
            _map.SetTiles(gridUid, grid, _wfMicroTiles);
    }

    /// <summary>
    /// How far apart a new explosion and a queued one can be to merge: further than the type allows when both are
    /// small and on the same grid.
    /// </summary>
    private float WFCombineDistance(QueuedExplosion queued, EntityCoordinates epicenter, float totalIntensity, float distance)
    {
        // A type that never combines stays that way.
        if (distance <= 0f
            || queued.Epicenter.EntityId != epicenter.EntityId
            || queued.TotalIntensity + totalIntensity > _wfBattleHeat.MergeMaxTotal
            || !HasComp<MapGridComponent>(epicenter.EntityId))
            return distance;

        return MathF.Max(distance, _wfBattleHeat.MergeDistance);
    }

    /// <summary>Counts a queued explosion towards the battle heat at its spot.</summary>
    private void WFRecordExplosion(EntityCoordinates epicenter)
    {
        _wfBattleHeat.Record(_transformSystem.ToMapCoordinates(epicenter));
    }
}
