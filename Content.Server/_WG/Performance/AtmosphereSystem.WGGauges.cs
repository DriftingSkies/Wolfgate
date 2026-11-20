using Content.Server.Atmos.Components;
using Prometheus;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    private static readonly Gauge WGLoadGauge = Metrics.CreateGauge(
        "wg_atmos_load",
        "Atmos work waiting on grid atmospheres, split by whether a player is near the grid",
        new GaugeConfiguration { LabelNames = ["measure", "attended"] });

    private static readonly Counter WGWindSearchCounter = Metrics.CreateCounter(
        "wg_atmos_wind_searches_total",
        "High-pressure tiles whose entities space wind searched");

    private static readonly Counter WGWindSkipCounter = Metrics.CreateCounter(
        "wg_atmos_wind_skips_total",
        "High-pressure tiles whose wind was too weak to push anything, so the search was skipped");

    private readonly int[] _wgLoad = new int[(int) WGAtmosMeasure.Count * 2];

    /// <summary>Sets the atmos load gauges. <paramref name="attended"/> holds the grids with a player near.</summary>
    public void WGSampleLoad(HashSet<EntityUid> attended)
    {
        Array.Clear(_wgLoad);
        var query = EntityQueryEnumerator<GridAtmosphereComponent>();
        while (query.MoveNext(out var uid, out var atmos))
        {
            if (!atmos.Simulated || Paused(uid))
                continue;

            var offset = attended.Contains(uid) ? 0 : (int) WGAtmosMeasure.Count;
            _wgLoad[offset + (int) WGAtmosMeasure.Grids]++;
            _wgLoad[offset + (int) WGAtmosMeasure.Tiles] += atmos.Tiles.Count;
            _wgLoad[offset + (int) WGAtmosMeasure.ActiveTiles] += atmos.ActiveTiles.Count;
            _wgLoad[offset + (int) WGAtmosMeasure.HighPressureTiles] += atmos.HighPressureDelta.Count;
            _wgLoad[offset + (int) WGAtmosMeasure.ExcitedGroups] += atmos.ExcitedGroups.Count;
            _wgLoad[offset + (int) WGAtmosMeasure.Hotspots] += atmos.HotspotTiles.Count;
            _wgLoad[offset + (int) WGAtmosMeasure.Devices] += atmos.AtmosDevices.Count;
            _wgLoad[offset + (int) WGAtmosMeasure.PipeNets] += atmos.PipeNets.Count;
            _wgLoad[offset + (int) WGAtmosMeasure.DeltaPressureEntities] += atmos.DeltaPressureEntities.Count;
        }

        for (var i = 0; i < (int) WGAtmosMeasure.Count; i++)
        {
            var measure = WGAtmosMeasureNames[i];
            WGLoadGauge.WithLabels(measure, "true").Set(_wgLoad[i]);
            WGLoadGauge.WithLabels(measure, "false").Set(_wgLoad[(int) WGAtmosMeasure.Count + i]);
        }

        WGWindSearchCounter.Inc(_wgWindSearches);
        WGWindSkipCounter.Inc(_wgWindSkips);
        _wgWindSearches = 0;
        _wgWindSkips = 0;
    }

    private static readonly string[] WGAtmosMeasureNames =
    [
        "grids", "tiles", "active_tiles", "high_pressure_tiles", "excited_groups", "hotspots", "devices", "pipe_nets",
        "delta_pressure_entities",
    ];

    private enum WGAtmosMeasure
    {
        Grids,
        Tiles,
        ActiveTiles,
        HighPressureTiles,
        ExcitedGroups,
        Hotspots,
        Devices,
        PipeNets,
        DeltaPressureEntities,
        Count,
    }
}
