#nullable enable

using System.Numerics;
using Content.Client._WG.CombatConsole;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._WG.Cockpit;

/// <summary>Four permanent collision lamps with independent alert flash rates and full target details on hover.</summary>
public sealed partial class WGCockpitTcasPanel : Control
{
    [Dependency] private IGameTiming _timing = default!;
    private readonly Func<WGCockpitTcasReading> _read;
    private readonly string _tcas = Loc.GetString("wg-cockpit-tcas-label");
    private readonly string _caution = Loc.GetString("wg-cockpit-tcas-caution");
    private readonly string _warning = Loc.GetString("wg-cockpit-tcas-warning");
    private readonly string _ok = Loc.GetString("wg-cockpit-tcas-ok");
    private readonly string _fault = Loc.GetString("wg-cockpit-tcas-fault");
    private WGCockpitTcasReading _reading;
    private string? _fontSkin;
    private Font _small = default!;
    private Font _large = default!;
    private float _poll;

    /// <summary>The current authoritative collision sample, independent of the lamp flash phase.</summary>
    public WGCockpitTcasReading Reading => _read();

    /// <summary>Uses the original banner's grid reader while remaining permanently visible.</summary>
    public WGCockpitTcasPanel(Func<WGCockpitTcasReading> read)
    {
        IoCManager.InjectDependencies(this);
        _read = read;
        Name = "CockpitTcas";
        HorizontalExpand = true;
        MinWidth = 200;
        SetHeight = 108;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Pass;
        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _poll += args.DeltaSeconds;
        if (_poll < 0.1f)
            return;
        _poll = 0;
        Refresh();
    }

    private void Refresh()
    {
        _reading = Reading;
        if (_reading.State is WGCockpitTcasState.Caution or WGCockpitTcasState.Warning)
        {
            var seconds = MathF.Max(0, (float) (_reading.ImpactTime - _timing.CurTime).TotalSeconds).ToString("0.0");
            var bearing = MathF.Round(_reading.Bearing).ToString("000");
            var speed = MathF.Round(_reading.ClosingSpeed).ToString();
            ToolTip = Loc.GetString("collision-warning-detail",
                ("threat", _reading.ThreatName ?? Loc.GetString("collision-warning-unknown-threat")),
                ("bearing", bearing), ("seconds", seconds), ("speed", speed));
        }
        else
        {
            ToolTip = Loc.GetString(_reading.State switch
            {
                WGCockpitTcasState.Clear => "wg-cockpit-tcas-ok-detail",
                WGCockpitTcasState.Off => "wg-cockpit-tcas-off-detail",
                _ => "wg-cockpit-tcas-unavailable-detail",
            });
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (PixelWidth < 1 || PixelHeight < 1)
            return;
        var skin = WGInstrumentTheme.Skin;
        if (_fontSkin != skin.Id)
        {
            var cache = IoCManager.Resolve<IResourceCache>();
            _small = cache.GetFont(skin.MenuFonts, 10);
            _large = cache.GetFont(skin.MenuFonts, 14);
            _fontSkin = skin.Id;
        }
        var gap = 4 * UIScale;
        var width = (PixelWidth - gap) / 2;
        var lampSize = new Vector2(width, 52 * UIScale);
        var phase = _reading.LampsAt(_timing.RealTime);
        DrawLamp(handle, UIBox2.FromDimensions(Vector2.Zero, lampSize), _caution, skin.Caution, phase.Caution);
        DrawLamp(handle, UIBox2.FromDimensions(new Vector2(width + gap, 0), lampSize), _warning, skin.Danger, phase.Warning);
        DrawLamp(handle, UIBox2.FromDimensions(new Vector2(0, lampSize.Y + gap), lampSize), _ok, skin.Good, phase.Ok);
        DrawLamp(handle, UIBox2.FromDimensions(new Vector2(width + gap, lampSize.Y + gap), lampSize), _fault, skin.Caution, phase.Fault);
    }

    /// <summary>Illuminates the entire legend through glass, retaining readable dark legends when inactive.</summary>
    private void DrawLamp(DrawingHandleScreen handle, UIBox2 box, string caption, Color color, bool lit)
    {
        var skin = WGInstrumentTheme.Skin;
        var inner = WGConsoleAnnunciator.Face(handle, box, UIScale, color, lit);
        var text = lit ? WGInstrumentTheme.Digital ? color : skin.Ink : skin.TextMuted;
        Text(handle, _small, _tcas, new UIBox2(inner.Left + 3 * UIScale, box.Top + 7 * UIScale, inner.Right - 3 * UIScale, box.Top + 25 * UIScale), text);
        Text(handle, _large, caption, new UIBox2(inner.Left + 3 * UIScale, box.Top + 23 * UIScale, inner.Right - 3 * UIScale, box.Top + 46 * UIScale), text);
    }

    private void Text(DrawingHandleScreen handle, Font font, string text, UIBox2 box, Color color)
    {
        var scale = WGInstrumentText.FitScale(handle, font, text, UIScale, box.Width, box.Height);
        if (scale == 0)
            return;
        var width = handle.GetDimensions(font, text, scale).X;
        handle.DrawString(font, box.Center - new Vector2(width / 2, font.GetHeight(scale) / 2f), text, scale, color);
    }
}
