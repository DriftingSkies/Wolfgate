using System.Numerics;
using Content.Server._NF.Shuttles.Components;
using Content.Server._WF.Fauna;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Planets;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._WF.OutpostRaids;

public sealed partial class WFRaidDirectorSystem
{
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    /// <summary>Metres between a raid's shuttle and the spot its crew walks in from.</summary>
    private const float ShuttleGap = 2f;

    /// <summary>Props of finished raids, standing until dawn on their ground.</summary>
    private readonly List<(EntityUid Prop, EntityUid Ground)> _lingering = new();

    /// <summary>Sends the raid's spawns in at its walk-in spot: mobs walk on, burrows open there.</summary>
    private void SendWave(WFActiveRaid raid)
    {
        foreach (var spawn in raid.Pack.Spawns)
        {
            var count = _random.Next(spawn.Min, spawn.Max + 1);
            for (var i = 0; i < count; i++)
            {
                var spawned = Spawn(spawn.Id, raid.Spot.Offset(_random.NextVector2(1f)));
                if (HasComp<WFRaidBurrowComponent>(spawned))
                {
                    raid.Burrows.Add(spawned);
                    continue;
                }

                SetUpMob(spawned, raid, raid.Edge);
                raid.Mobs.Add(spawned);
            }
        }
    }

    /// <summary>Takes in what a raid's burrows spawned; a destroyed burrow counts as killed, and at dawn they collapse.</summary>
    private void UpdateBurrows(WFActiveRaid raid, bool dawn)
    {
        for (var i = raid.Burrows.Count - 1; i >= 0; i--)
        {
            var burrow = raid.Burrows[i];
            if (TerminatingOrDeleted(burrow))
            {
                raid.Killed++;
                raid.Burrows.RemoveAt(i);
                continue;
            }

            if (dawn)
            {
                raid.Despawned++;
                raid.Burrows.RemoveAt(i);
                QueueDel(burrow);
                continue;
            }

            if (!TryComp<FaunaBurrowComponent>(burrow, out var hole))
                continue;

            foreach (var spawned in hole.Spawned)
            {
                if (TerminatingOrDeleted(spawned) || HasComp<WFRaidMobComponent>(spawned))
                    continue;
                SetUpMob(spawned, raid, raid.Edge);
                raid.Mobs.Add(spawned);
            }
        }
    }

    /// <summary>A finished raid's props go at dawn, or now if it is dawn already.</summary>
    private void Leave(WFActiveRaid raid, bool dawn)
    {
        foreach (var prop in raid.Props)
        {
            if (dawn)
                QueueDel(prop);
            else
                _lingering.Add((prop, raid.Ground));
        }
    }

    private void UpdateLingering()
    {
        for (var i = _lingering.Count - 1; i >= 0; i--)
        {
            var (prop, ground) = _lingering[i];
            if (TerminatingOrDeleted(prop))
            {
                _lingering.RemoveAt(i);
                continue;
            }

            if (!TerminatingOrDeleted(ground) && _window.TryGetWindowOn(ground, out var open, out _) && open)
                continue;

            QueueDel(prop);
            _lingering.RemoveAt(i);
        }
    }

    /// <summary>
    /// Sets the raid's shuttle down on the ground just beyond its walk-in spot, away from the claim, and holds it there
    /// as an outpost is held. Refused when it would land on another grid or outside the world.
    /// </summary>
    private bool TryLandShuttle(WFActiveRaid raid, ResPath path)
    {
        var mapId = Transform(raid.Ground).MapID;
        if (!_mapLoader.TryLoadGrid(mapId, path, out var loaded))
        {
            Log.Warning($"Couldn't load the raid shuttle {path}.");
            return false;
        }

        var grid = loaded.Value;
        var box = grid.Comp.LocalAABB;
        var spotAt = _transform.ToMapCoordinates(raid.Spot).Position;
        var outward = spotAt - _transform.ToMapCoordinates(raid.Edge).Position;
        outward = outward.LengthSquared() > 0.01f ? Vector2.Normalize(outward) : Vector2.UnitX;
        var centre = spotAt + outward * (box.Size.Length() / 2f + ShuttleGap);
        _transform.SetWorldPosition(grid.Owner, centre - box.Center);

        var clear = true;
        _nearGrids.Clear();
        _maps.FindGridsIntersecting(mapId, Box2.CenteredAround(centre, box.Size + new Vector2(2f)), ref _nearGrids, approx: true, includeMap: false);
        foreach (var near in _nearGrids)
        {
            if (near.Owner != grid.Owner)
                clear = false;
        }

        var local = Vector2.Transform(centre, _transform.GetInvWorldMatrix(raid.Ground));
        if (CompOrNull<WFPlanetBoundsComponent>(raid.Ground) is { } bounds && !bounds.Contains(local))
            clear = false;

        if (!clear)
        {
            Del(grid);
            return false;
        }

        // Loaded on a running map, so ForceAnchor's own map-init hold never runs; hold it here.
        EnsureComp<ForceAnchorComponent>(grid);
        EnsureComp<PreventGridAnchorChangesComponent>(grid);
        _shuttle.Disable(grid, force: true);
        EnsureComp<WFRaidShuttleComponent>(grid).Raid = raid.Id;
        _loot.MarkGrid(grid);
        raid.Props.Add(grid);
        return true;
    }
}
