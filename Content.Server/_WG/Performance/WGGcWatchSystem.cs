using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._WG.Performance;

/// <summary>Server cvars for <see cref="WGGcWatchSystem"/>.</summary>
[CVarDefs]
public sealed class WGGcWatchCVars
{
    /// <summary>Garbage collection pausing the server at least this many milliseconds in a second is logged; 0 turns it off.</summary>
    public static readonly CVarDef<float> PauseWarnMs =
        CVarDef.Create("wg.gc.pause_warn_ms", 500f, CVar.SERVERONLY);

    /// <summary>A gap of at least this many seconds between ticks is logged with how much of it was GC; 0 turns it off.</summary>
    public static readonly CVarDef<float> StallWarnSeconds =
        CVarDef.Create("wg.gc.stall_warn_seconds", 2f, CVar.SERVERONLY);
}

/// <summary>What the garbage collector had done by one moment: its total pause, collections and heap sizes.</summary>
public readonly record struct WGGcReading(
    TimeSpan Pause,
    int Gen0,
    int Gen1,
    int Gen2,
    long Gen0Bytes,
    long Gen1Bytes,
    long Gen2Bytes,
    long LargeBytes,
    long PinnedBytes,
    long HeapBytes,
    long FragmentedBytes,
    long CommittedBytes);

/// <summary>
/// Logs garbage collection that pauses the server and every long gap between ticks with how much of it was GC, and
/// exports <c>wg_gc_heap_bytes{part}</c> and <c>wg_gc_pause_seconds_total</c>. A whole-process freeze stops this too,
/// so its line is written on the first tick after.
/// </summary>
public sealed class WGGcWatchSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Gauge HeapGauge = Metrics.CreateGauge(
        "wg_gc_heap_bytes",
        "Managed heap after the last garbage collection, by part",
        new GaugeConfiguration { LabelNames = ["part"] });

    private static readonly Counter PauseCounter = Metrics.CreateCounter(
        "wg_gc_pause_seconds_total",
        "Time garbage collection has paused the server");

    private static readonly TimeSpan GaugeEvery = TimeSpan.FromSeconds(5);
    private static readonly Func<WGGcReading> ReadFull = Read;

    private float _pauseWarnMs;
    private float _stallWarnSeconds;
    private TimeSpan _nextGauge;
    private readonly WGGcClock _clock = new();

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, WGGcWatchCVars.PauseWarnMs, value => _pauseWarnMs = value, true);
        Subs.CVar(_cfg, WGGcWatchCVars.StallWarnSeconds, value => _stallWarnSeconds = value, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.RealTime;
        var step = _clock.Tick(now, ReadCounts(), ReadFull, _stallWarnSeconds, _pauseWarnMs);
        if (!step.Sampled)
            return;

        if (step.Paused > TimeSpan.Zero)
            PauseCounter.Inc(step.Paused.TotalSeconds);

        if (step.Warning != null)
            Log.Warning(step.Warning);

        if (now >= _nextGauge)
        {
            _nextGauge = now + GaugeEvery;
            SetGauges(step.Reading);
        }
    }

    /// <summary>The collector's pause and collection counts as of now, without heap sizes; cheap enough for every tick.</summary>
    public static WGGcReading ReadCounts()
    {
        return new WGGcReading(GC.GetTotalPauseDuration(), GC.CollectionCount(0), GC.CollectionCount(1),
            GC.CollectionCount(2), 0, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>The collector's totals and heap sizes as of now.</summary>
    public static WGGcReading Read()
    {
        var info = GC.GetGCMemoryInfo(GCKind.Any);
        var generations = info.GenerationInfo;

        return new WGGcReading(
            GC.GetTotalPauseDuration(),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            Size(generations, 0),
            Size(generations, 1),
            Size(generations, 2),
            Size(generations, 3),
            Size(generations, 4),
            info.HeapSizeBytes,
            info.FragmentedBytes,
            info.TotalCommittedBytes);
    }

    /// <summary>A gap between ticks, with how much of it the collector paused and what it collected.</summary>
    public static string StallReport(TimeSpan gap, WGGcReading before, WGGcReading after)
    {
        var paused = after.Pause - before.Pause;
        var share = gap > TimeSpan.Zero ? paused / gap : 0;
        var cause = share >= 0.5 ? "mostly garbage collection" : "not garbage collection";
        return $"Server stalled {gap.TotalSeconds:F1} s between ticks, {cause}: GC paused {paused.TotalMilliseconds:F0} ms " +
               $"({Collections(before, after)}); heap {Mb(after.HeapBytes)} MB, gen2 {Mb(after.Gen2Bytes)} MB, " +
               $"large objects {Mb(after.LargeBytes)} MB.";
    }

    /// <summary>Collector pauses over a stretch of normal ticks.</summary>
    public static string PauseReport(TimeSpan over, WGGcReading before, WGGcReading after)
    {
        var paused = after.Pause - before.Pause;
        return $"GC paused the server {paused.TotalMilliseconds:F0} ms in {over.TotalSeconds:F1} s " +
               $"({Collections(before, after)}); heap {Mb(after.HeapBytes)} MB, gen2 {Mb(after.Gen2Bytes)} MB, " +
               $"large objects {Mb(after.LargeBytes)} MB.";
    }

    private static string Collections(WGGcReading before, WGGcReading after) =>
        $"{after.Gen2 - before.Gen2} gen2, {after.Gen1 - before.Gen1} gen1, {after.Gen0 - before.Gen0} gen0 collections";

    private static long Mb(long bytes) => bytes / (1024 * 1024);

    /// <summary>Gen0, gen1, gen2, large and pinned object heaps, in that order.</summary>
    private static long Size(ReadOnlySpan<GCGenerationInfo> generations, int index) =>
        index < generations.Length ? generations[index].SizeAfterBytes : 0;

    private static void SetGauges(WGGcReading reading)
    {
        HeapGauge.WithLabels("gen0").Set(reading.Gen0Bytes);
        HeapGauge.WithLabels("gen1").Set(reading.Gen1Bytes);
        HeapGauge.WithLabels("gen2").Set(reading.Gen2Bytes);
        HeapGauge.WithLabels("loh").Set(reading.LargeBytes);
        HeapGauge.WithLabels("poh").Set(reading.PinnedBytes);
        HeapGauge.WithLabels("heap").Set(reading.HeapBytes);
        HeapGauge.WithLabels("fragmented").Set(reading.FragmentedBytes);
        HeapGauge.WithLabels("committed").Set(reading.CommittedBytes);
    }
}
