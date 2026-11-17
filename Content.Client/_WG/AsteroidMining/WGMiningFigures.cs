using System.Numerics;
using Content.Client._WG.CombatConsole;
using Content.Shared._WG.AsteroidMining;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WG.AsteroidMining;

/// <summary>The rock's cracking window, crack progress and yield as three figure windows in a strip under the heat dial.</summary>
public sealed class WGMiningFigures : Control
{
    private readonly Font _small;
    private readonly Font _digits;
    private readonly string[] _captions =
    {
        Loc.GetString("wg-mining-figure-window"),
        Loc.GetString("wg-mining-figure-crack"),
        Loc.GetString("wg-mining-figure-yield"),
    };
    private readonly string[] _values = new string[3];
    private readonly string _unit = Loc.GetString("wg-mining-dial-unit");
    private readonly string _empty = Loc.GetString("wg-mining-dial-empty");
    private WGMiningBeamTargetEntry? _target;

    public WGMiningFigures()
    {
        MinSize = new Vector2(0, 40);
        HorizontalExpand = true;
        MouseFilter = MouseFilterMode.Pass;
        ToolTip = Loc.GetString("wg-mining-figures-help");
        var cache = IoCManager.Resolve<IResourceCache>();
        _small = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 9);
        _digits = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 16);
    }

    /// <summary>Shows a rock's figures, or dashes for none; they are formatted here rather than every frame.</summary>
    public void SetTarget(WGMiningBeamTargetEntry? target)
    {
        _target = target;
        if (target is not { } rock)
            return;

        _values[0] = Loc.GetString("wg-mining-figure-window-range", ("low", Percent(rock.WindowLow)), ("high", Percent(rock.WindowHigh)));
        _values[1] = Percent(rock.Progress).ToString();
        _values[2] = Percent(rock.Yield).ToString();
    }

    private static int Percent(float value)
    {
        return (int) MathF.Round(value * 100);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var skin = WGInstrumentTheme.Skin;
        var digital = WGInstrumentTheme.Digital;
        var gap = 3 * UIScale;
        var width = (PixelWidth - gap * (_captions.Length - 1)) / _captions.Length;
        for (var i = 0; i < _captions.Length; i++)
        {
            var box = UIBox2.FromDimensions(new Vector2(i * (width + gap), 0), new Vector2(width, PixelHeight));
            var inner = new UIBox2(box.TopLeft + new Vector2(3 * UIScale), box.BottomRight - new Vector2(3 * UIScale));
            if (digital)
                WGConsoleDigital.Panel(handle, box, UIScale, skin.Glass, skin.EdgeSoft, false, 4);
            else
                WGConsoleMetal.Bevel(handle, box, UIScale, skin.Ink, skin.EdgeSoft, inset: true);

            var caption = new UIBox2(inner.Left, inner.Top, inner.Right, inner.Top + inner.Height * 0.32f);
            var value = new UIBox2(inner.Left, caption.Bottom + UIScale, inner.Right, inner.Bottom);
            WGInstrumentText.DrawFitted(handle, _small, _captions[i], caption, UIScale, skin.TextMuted);
            if (_target is { } rock)
            {
                var color = i switch
                {
                    0 => skin.Good,
                    1 => skin.Accent,
                    _ => rock.Yield >= 0.995f ? skin.Good : skin.Caution,
                };
                WGInstrumentText.DrawReading(handle, _digits, _values[i], _unit, value, UIScale, color, skin.TextMuted);
            }
            else
            {
                WGInstrumentText.DrawFitted(handle, _digits, _empty, value, UIScale, skin.TextMuted.WithAlpha(0.6f));
            }

            if (!digital)
                WGInstrumentGlass.Window(handle, inner, UIScale);
        }
    }
}
