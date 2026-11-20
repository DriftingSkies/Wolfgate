using Content.Client._WG.Shuttles.UI;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WG.Augments.UI;

/// <summary>
/// The quarter-second of interference when thermal tint eyes switch band: tearing static bands in the new tint and
/// black over a scanline grid, fading out.
/// </summary>
public sealed partial class WGThermalGlitchOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;

    private const float Duration = 0.25f;

    /// <summary>Static bands drawn each frame at the start; they thin out as the glitch fades.</summary>
    private const int Bands = 14;

    /// <summary>Pixels between scanlines.</summary>
    private const int ScanlineGap = 3;

    private TimeSpan? _start;
    private Color _color;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public WGThermalGlitchOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = 6;
    }

    public void Start(Color color)
    {
        _start = _timing.RealTime;
        _color = color;
    }

    private float Progress => _start is { } start ? (float) (_timing.RealTime - start).TotalSeconds / Duration : 1f;

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (Progress >= 1f)
        {
            _start = null;
            return false;
        }

        return _player.LocalEntity is { } player
               && _entities.TryGetComponent<EyeComponent>(player, out var eye) && eye.Eye != null
               && ShuttleHullRoofOverlay.IsPilotPass(args.Viewport.Eye, eye.Eye, primaryOnly: true);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var bounds = args.ViewportBounds;
        var fade = 1f - Progress;

        // A flash of the new band that drops off fast, so the swap reads before the static does.
        handle.DrawRect(bounds, _color.WithAlpha(0.3f * fade * fade));

        var scanline = Color.Black.WithAlpha(0.3f * fade);
        for (var y = bounds.Top; y < bounds.Bottom; y += ScanlineGap)
            handle.DrawRect(new UIBox2(bounds.Left, y, bounds.Right, y + 1), scanline);

        var height = bounds.Height;
        var width = bounds.Width;
        var bands = (int) MathF.Ceiling(Bands * fade);
        for (var i = 0; i < bands; i++)
        {
            var top = bounds.Top + _random.NextFloat() * height;
            var thick = 2f + _random.NextFloat() * height * 0.03f;
            // Each band tears sideways: a solid run offset from one edge, broken by a gap.
            var start = bounds.Left + _random.NextFloat(-0.2f, 0.6f) * width;
            var run = _random.NextFloat(0.2f, 0.9f) * width;
            var color = _random.Prob(0.6f)
                ? _color.WithAlpha(_random.NextFloat(0.35f, 0.7f) * fade)
                : Color.Black.WithAlpha(_random.NextFloat(0.5f, 0.8f) * fade);
            handle.DrawRect(new UIBox2(start, top, start + run, top + thick), color);

            // A few bright specks of snow along the band.
            for (var s = 0; s < 6; s++)
            {
                var x = bounds.Left + _random.NextFloat() * width;
                var y = top + _random.NextFloat() * thick;
                handle.DrawRect(new UIBox2(x, y, x + 2f, y + 2f), Color.White.WithAlpha(0.6f * fade));
            }
        }
    }
}
