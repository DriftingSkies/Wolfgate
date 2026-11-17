namespace Content.Client._WG.CargoLoad;

/// <summary>Refreshes the cargo strips on screen, which can't poll themselves while hidden.</summary>
public sealed class WGCargoGaugeSystem : EntitySystem
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(0.25);

    private readonly HashSet<WGCargoGauge> _gauges = new();
    private readonly List<WGCargoGauge> _polled = new();
    private TimeSpan _poll;

    public void Track(WGCargoGauge gauge)
    {
        _gauges.Add(gauge);
    }

    public void Untrack(WGCargoGauge gauge)
    {
        _gauges.Remove(gauge);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        _poll += TimeSpan.FromSeconds(frameTime);
        if (_poll < PollInterval)
            return;

        _poll = TimeSpan.Zero;
        _polled.Clear();
        _polled.AddRange(_gauges);
        foreach (var gauge in _polled)
        {
            gauge.Refresh();
        }
    }
}
