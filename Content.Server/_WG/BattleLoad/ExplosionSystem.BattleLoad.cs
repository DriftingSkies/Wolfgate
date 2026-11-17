using Content.Server._WG.BattleLoad;
using Content.Server.Explosion.Components;
using Content.Shared.Atmos;
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
    [Dependency] private WGBattleHeatSystem _wgBattleHeat = default!;

    /// <summary>The epicentre tile and its four neighbours: the tiles a bullet-sized blast reaches.</summary>
    private static readonly Vector2i[] WGMicroOffsets =
    {
        new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    /// <summary>The direction from the epicentre to each of <see cref="WGMicroOffsets"/>.</summary>
    private static readonly AtmosDirection[] WGMicroDirections =
    {
        AtmosDirection.Invalid, AtmosDirection.East, AtmosDirection.West, AtmosDirection.North, AtmosDirection.South,
    };

    private const int WGMicroMaxPerTick = 40;
    private static readonly TimeSpan WGMicroMaxTime = TimeSpan.FromMilliseconds(3);

    private readonly List<WGMicroExplosion> _wgMicroQueue = new();
    private readonly List<WGMicroExplosion> _wgMicroBatch = new();
    private readonly HashSet<EntityUid> _wgMicroProcessed = new();
    private readonly List<(Vector2i GridIndices, Tile Tile)> _wgMicroTiles = new();

    private readonly record struct WGMicroExplosion(
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
    private bool WGTryQueueMicroExplosion(EntityCoordinates epicenter,
        string typeId,
        float totalIntensity,
        float maxTileIntensity,
        float tileBreakScale,
        int maxTileBreak,
        bool canCreateVacuum,
        EntityUid? cause)
    {
        if (!_wgBattleHeat.MicroExplosions
            || totalIntensity <= 0
            || totalIntensity > _wgBattleHeat.MicroMaxTotal
            || maxTileIntensity > _wgBattleHeat.MicroMaxTile
            || !HasComp<MapGridComponent>(epicenter.EntityId)
            || !_prototypeManager.TryIndex<ExplosionPrototype>(typeId, out var type))
            return false;

        _wgBattleHeat.Record(_transformSystem.ToMapCoordinates(epicenter));
        _wgMicroQueue.Add(new WGMicroExplosion(epicenter, type, totalIntensity, maxTileIntensity, tileBreakScale,
            maxTileBreak, canCreateVacuum, cause));
        return true;
    }

    /// <summary>Applies the bullet-sized explosions queued since the last update.</summary>
    private void WGProcessMicroExplosions()
    {
        if (_wgMicroQueue.Count == 0)
            return;

        // A blast set off by one of these, a fuel tank say, waits for the next tick.
        _wgMicroBatch.AddRange(_wgMicroQueue);
        _wgMicroQueue.Clear();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var done = 0;
        try
        {
            // Past the per-tick cap the rest wait, ahead of anything queued by these blasts.
            while (done < _wgMicroBatch.Count && done < WGMicroMaxPerTick && watch.Elapsed < WGMicroMaxTime)
            {
                WGApplyMicroExplosion(_wgMicroBatch[done]);
                done++;
            }
        }
        finally
        {
            if (done < _wgMicroBatch.Count)
                _wgMicroQueue.InsertRange(0, _wgMicroBatch.GetRange(done, _wgMicroBatch.Count - done));

            _wgMicroBatch.Clear();
        }
    }

    /// <summary>
    /// Damages the epicentre tile at up to the blast's tile intensity and spreads what is left over its four
    /// neighbours, through the same per-tile code as a full explosion.
    /// </summary>
    private void WGApplyMicroExplosion(WGMicroExplosion micro)
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
        // A blast inside a standing wall leaves it only through the sides the wall leaves open, as a full explosion does.
        // A wall the blast at its centre breaks, one already weakened say, no longer holds it.
        var blocked = AtmosDirection.Invalid;
        if (TryComp<ExplosionAirtightGridComponent>(gridUid, out var airtight)
            && airtight.Tiles.TryGetValue(center, out var centerData)
            && !(_explosionTypes.TryGetValue(micro.Proto.ID, out var typeIndex)
                 && (float) GetToleranceValues(centerData.ToleranceCacheIndex).Values[typeIndex] <= centerIntensity))
        {
            blocked = centerData.BlockedDirections;
        }

        _wgMicroProcessed.Clear();
        _wgMicroTiles.Clear();
        for (var i = 0; i < WGMicroOffsets.Length; i++)
        {
            var intensity = i == 0 ? centerIntensity : ringIntensity;
            if (intensity <= 0f || i > 0 && blocked.IsFlagSet(WGMicroDirections[i]))
                continue;

            var tile = center + WGMicroOffsets[i];
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
                _wgMicroProcessed,
                micro.Proto.ID,
                micro.Proto.FireStacks,
                micro.Proto.Temperature,
                intensity,
                micro.Cause);

            if (canDamageFloor)
                DamageFloorTile(tileRef, intensity * micro.TileBreakScale, micro.MaxTileBreak, micro.CanCreateVacuum, _wgMicroTiles, micro.Proto);
        }

        if (_wgMicroTiles.Count > 0 && !TerminatingOrDeleted(gridUid))
            _map.SetTiles(gridUid, grid, _wgMicroTiles);
    }

    /// <summary>
    /// How far apart a new explosion and a queued one can be to merge: further than the type allows when both are
    /// small and on the same grid.
    /// </summary>
    private float WGCombineDistance(QueuedExplosion queued, EntityCoordinates epicenter, float totalIntensity, float distance)
    {
        // A type that never combines stays that way.
        if (distance <= 0f
            || queued.Epicenter.EntityId != epicenter.EntityId
            || queued.TotalIntensity + totalIntensity > _wgBattleHeat.MergeMaxTotal
            || !HasComp<MapGridComponent>(epicenter.EntityId))
            return distance;

        return MathF.Max(distance, _wgBattleHeat.MergeDistance);
    }

    /// <summary>Counts a queued explosion towards the battle heat at its spot.</summary>
    private void WGRecordExplosion(EntityCoordinates epicenter)
    {
        _wgBattleHeat.Record(_transformSystem.ToMapCoordinates(epicenter));
    }
}
