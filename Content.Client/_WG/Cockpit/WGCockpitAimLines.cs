using System.Numerics;
using Content.Client.Shuttles.UI;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Cockpit;

/// <summary>Draws the gunnery console's weapon-to-aim preview lines over the cockpit NAV plot.</summary>
public sealed class WGCockpitAimLines : Control
{
    /// <summary>The console's own radar draws these lines at this alpha.</summary>
    private const float PlotAlpha = 0.3f;

    private readonly ShuttleNavControl _navigation;
    private readonly WGCockpitAimPreview _preview;
    private readonly List<WGCockpitAimSegment> _plot = new();

    public WGCockpitAimLines(ShuttleNavControl navigation, WGCockpitAimPreview preview)
    {
        _navigation = navigation;
        _preview = preview;
        MouseFilter = MouseFilterMode.Ignore;
    }

    /// <summary>The preview lines in plot pixels; empty while the plot shows another map or nothing is aimed.</summary>
    public IReadOnlyList<WGCockpitAimSegment> Lines()
    {
        _plot.Clear();
        var segments = _preview.Refresh();
        if (segments.Count == 0 || !_navigation.WgCockpitTryGetWorldToView(out var worldToView, out var map) ||
            map != _preview.Map)
            return _plot;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            _plot.Add(new WGCockpitAimSegment(Vector2.Transform(segment.From, worldToView),
                Vector2.Transform(segment.To, worldToView), segment.Color));
        }

        return _plot;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var lines = Lines();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            handle.DrawLine(line.From, line.To, line.Color.WithAlpha(PlotAlpha));
        }
    }
}
