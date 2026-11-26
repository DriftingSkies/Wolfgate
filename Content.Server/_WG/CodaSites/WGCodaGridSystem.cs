using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.Components;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.CodaSites;

/// <summary>
/// What keeps a Coda site standing on its own: power that needs no plant (WGSelfPoweredGrid), air that never drains
/// (WGFixedAtmosphere), and a hull that heals and grows back (WGHullRegen).
/// </summary>
public sealed class WGCodaGridSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedPowerReceiverSystem _receiver = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateHull(_timing.CurTime);
    }

    private const float GasConstant = 8.314462618f;
    private const float TileVolume = Atmospherics.CellVolume;

    private TimeSpan _nextPower;

    private void UpdateHull(TimeSpan now)
    {
        if (now >= _nextPower)
        {
            _nextPower = now + TimeSpan.FromSeconds(2);
            PowerGrids();
        }

        var air = EntityQueryEnumerator<WGFixedAtmosphereComponent, MapGridComponent, TransformComponent>();
        while (air.MoveNext(out var grid, out var fixedAir, out var gridComp, out var xform))
        {
            if (now < fixedAir.Next)
                continue;

            fixedAir.Next = now + fixedAir.Interval;
            Refill((grid, fixedAir, gridComp), xform.MapUid);
        }

        var hulls = EntityQueryEnumerator<WGHullRegenComponent, MapGridComponent>();
        while (hulls.MoveNext(out var grid, out var hull, out var gridComp))
        {
            if (now < hull.Next)
                continue;

            hull.Next = now + TimeSpan.FromSeconds(1);
            if (!hull.Recorded)
                Record((grid, hull, gridComp));
            Heal(hull, now);
            Regrow((grid, hull, gridComp), now);
        }
    }

    /// <summary>
    /// Marks every power receiver on a self-powered grid as needing no power, and gives power needs back to any that
    /// have left one. Only the site grids' own entities are walked, never the whole server's receivers.
    /// </summary>
    private void PowerGrids()
    {
        var exempt = EntityQueryEnumerator<WGSelfPoweredReceiverComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (exempt.MoveNext(out var uid, out var mark, out var receiver, out var xform))
        {
            if (xform.GridUid == mark.Grid && !TerminatingOrDeleted(mark.Grid))
                continue;

            _receiver.SetNeedsPower(uid, true, receiver);
            RemCompDeferred<WGSelfPoweredReceiverComponent>(uid);
        }

        var grids = EntityQueryEnumerator<WGSelfPoweredGridComponent, TransformComponent>();
        while (grids.MoveNext(out var grid, out _, out var gridXform))
        {
            var children = gridXform.ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                // A receiver that already needed no power is left alone, so it stays that way after it leaves.
                if (!TryComp<ApcPowerReceiverComponent>(child, out var receiver) || !receiver.NeedsPower)
                    continue;

                _receiver.SetNeedsPower(child, false, receiver);
                EnsureComp<WGSelfPoweredReceiverComponent>(child).Grid = grid;
            }
        }
    }

    /// <summary>Sets every tile of the grid back to its one mix.</summary>
    private void Refill(Entity<WGFixedAtmosphereComponent, MapGridComponent> grid, EntityUid? map)
    {
        var (_, air, gridComp) = grid;
        var total = air.Pressure * TileVolume / (GasConstant * air.Temperature);
        var oxygen = air.OxygenPressure * TileVolume / (GasConstant * air.Temperature);
        foreach (var tile in _map.GetAllTiles(grid, gridComp))
        {
            // Every tile gets the same mix, so nothing needs to flow: tiles aren't woken in atmos, and a tile already
            // at the mix is left alone.
            var mix = _atmos.GetTileMixture(grid.Owner, map, tile.GridIndices);
            if (mix == null || mix.Immutable
                || MathF.Abs(mix.TotalMoles - total) < total * 0.01f && MathF.Abs(mix.Temperature - air.Temperature) < 1f)
                continue;

            mix.Clear();
            mix.SetMoles(Gas.Oxygen, oxygen);
            mix.SetMoles(Gas.Nitrogen, total - oxygen);
            mix.Temperature = air.Temperature;
        }
    }

    /// <summary>Records the hull and floor as mapped, so whatever is lost can grow back as it was.</summary>
    private void Record(Entity<WGHullRegenComponent, MapGridComponent> grid)
    {
        var (uid, hull, gridComp) = grid;
        hull.Recorded = true;
        foreach (var tile in _map.GetAllTiles(uid, gridComp))
        {
            hull.Floors[tile.GridIndices] = _tileDefs[tile.Tile.TypeId].ID;
            foreach (var ent in _map.GetAnchoredEntities(uid, gridComp, tile.GridIndices))
            {
                if (!HasComp<AirtightComponent>(ent) || MetaData(ent).EntityPrototype is not { } proto)
                    continue;

                hull.Hull[tile.GridIndices] = proto.ID;
                hull.Pieces[tile.GridIndices] = ent;
                break;
            }
        }
    }

    /// <summary>Mends a share of each piece's damage once it has gone unhit for a while.</summary>
    private void Heal(WGHullRegenComponent hull, TimeSpan now)
    {
        foreach (var (_, piece) in hull.Pieces)
        {
            if (TerminatingOrDeleted(piece) || !TryComp<DamageableComponent>(piece, out var damageable))
                continue;

            var total = damageable.TotalDamage;
            hull.Seen.TryGetValue(piece, out var seen);
            if (total > seen.Total)
                seen.Hit = now;
            seen.Total = total;
            hull.Seen[piece] = seen;

            if (total <= 0 || now - seen.Hit < hull.HealDelay)
                continue;

            _damageable.TryChangeDamage(piece, damageable.Damage * -hull.HealFraction, true, false);
            hull.Seen[piece] = (damageable.TotalDamage, seen.Hit);
        }
    }

    /// <summary>Grows back destroyed pieces and torn-up floor: flesh after a delay, then the piece itself.</summary>
    private void Regrow(Entity<WGHullRegenComponent, MapGridComponent> grid, TimeSpan now)
    {
        var (uid, hull, gridComp) = grid;
        foreach (var (index, floor) in hull.Floors)
        {
            if (_map.GetTileRef(uid, gridComp, index).Tile.IsEmpty && _tileDefs.TryGetDefinition(floor, out var def))
                _map.SetTile(uid, gridComp, index, new Tile(def.TileId));
        }

        foreach (var (index, proto) in hull.Hull)
        {
            if (hull.Pieces.TryGetValue(index, out var piece) && !TerminatingOrDeleted(piece))
                continue;

            if (!hull.Gone.TryGetValue(index, out var gone))
            {
                hull.Gone[index] = now;
                continue;
            }

            var coords = _map.GridTileToLocal(uid, gridComp, index);
            if (hull.Growing.TryGetValue(index, out var flesh))
            {
                // Flesh hardens a while after it appeared; if something has come to stand in it, it waits.
                if (now - hull.FleshAt.GetValueOrDefault(index, now) < hull.FleshTime || Occupied(uid, gridComp, index, flesh))
                    continue;

                QueueDel(flesh);
                hull.Growing.Remove(index);
                hull.FleshAt.Remove(index);
                hull.Pieces[index] = Spawn(proto, coords);
                hull.Gone.Remove(index);
                continue;
            }

            if (now - gone < hull.RegrowDelay || Occupied(uid, gridComp, index))
                continue;

            hull.Growing[index] = Spawn(hull.Flesh, coords);
            hull.FleshAt[index] = now;
        }
    }

    /// <summary>Whether anything anchored or alive stands on a tile, so nothing grows over it.</summary>
    private bool Occupied(EntityUid grid, MapGridComponent gridComp, Vector2i index, EntityUid? ignore = null)
    {
        foreach (var anchored in _map.GetAnchoredEntities(grid, gridComp, index))
        {
            if (anchored != ignore)
                return true;
        }

        foreach (var ent in _lookup.GetLocalEntitiesIntersecting(grid, index))
        {
            if (HasComp<MobStateComponent>(ent))
                return true;
        }

        return false;
    }
}
