using System.Numerics;
using System.Text;
using Content.Shared._WF.NanoChat;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WF.NanoChat;

/// <summary>
/// Wrapped text drawn glyph by glyph so each one can move or change colour: shaking, a travelling wave or a drifting
/// rainbow. Reduced motion keeps the glyphs still and the rainbow fixed.
/// </summary>
public sealed class NanoChatEffectText : Control
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Room above and below the lines for glyphs that move, in virtual pixels.</summary>
    private const float Headroom = 3f;

    private readonly List<Rune> _runes = new();

    /// <summary>Each line as the index of its first rune and the rune count.</summary>
    private readonly List<(int Start, int Length)> _lines = new();

    private NanoChatEffect _effect;

    public Color TextColor = Color.White;

    public NanoChatEffectText()
    {
        IoCManager.InjectDependencies(this);
    }

    public void SetText(string text, NanoChatEffect effect)
    {
        _runes.Clear();
        foreach (var rune in text.EnumerateRunes())
        {
            _runes.Add(rune);
        }

        _effect = effect;
        InvalidateMeasure();
    }

    private Font ActualFont => TryGetStyleProperty<Font>("font", out var font)
        ? font
        : UserInterfaceManager.ThemeDefaults.LabelFont;

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var font = ActualFont;
        var maxWidth = float.IsFinite(availableSize.X) ? availableSize.X * UIScale : float.MaxValue;
        var widest = Wrap(font, maxWidth);
        var height = _lines.Count * font.GetLineHeight(UIScale) / UIScale + Headroom * 2;
        return new Vector2(widest / UIScale, height);
    }

    /// <summary>Breaks the runes into lines no wider than the given pixels, at spaces where it can. Returns the widest.</summary>
    private float Wrap(Font font, float maxWidth)
    {
        _lines.Clear();
        var start = 0;
        var width = 0f;
        var widest = 0f;
        var lastSpace = -1;
        var widthAtSpace = 0f;
        for (var i = 0; i < _runes.Count; i++)
        {
            var advance = Advance(font, _runes[i]);
            if (width + advance > maxWidth && i > start)
            {
                if (lastSpace > start)
                {
                    _lines.Add((start, lastSpace - start));
                    widest = Math.Max(widest, widthAtSpace);
                    width -= widthAtSpace + Advance(font, _runes[lastSpace]);
                    start = lastSpace + 1;
                }
                else
                {
                    _lines.Add((start, i - start));
                    widest = Math.Max(widest, width);
                    width = 0;
                    start = i;
                }

                lastSpace = -1;
            }

            if (_runes[i] == new Rune(' '))
            {
                lastSpace = i;
                widthAtSpace = width;
            }

            width += advance;
        }

        if (start < _runes.Count)
        {
            _lines.Add((start, _runes.Count - start));
            widest = Math.Max(widest, width);
        }

        return widest;
    }

    private float Advance(Font font, Rune rune) => font.GetCharMetrics(rune, UIScale)?.Advance ?? 0;

    protected override void Draw(DrawingHandleScreen handle)
    {
        var font = ActualFont;
        var still = _cfg.GetCVar(CCVars.ReducedMotion);
        var time = still ? 0f : (float) _timing.RealTime.TotalSeconds;
        var ascent = font.GetAscent(UIScale);
        var lineHeight = font.GetLineHeight(UIScale);
        var top = Headroom * UIScale;

        for (var line = 0; line < _lines.Count; line++)
        {
            var (start, length) = _lines[line];
            var baseline = new Vector2(0, top + ascent + lineHeight * line);
            for (var i = start; i < start + length; i++)
            {
                var offset = still ? Vector2.Zero : Offset(i, time) * UIScale;
                var color = _effect == NanoChatEffect.Rainbow
                    ? Color.FromHsv(new Vector4((time * 0.25f + i * 0.06f) % 1f, 0.55f, 1f, 1f))
                    : TextColor;
                baseline.X += font.DrawChar(handle, _runes[i], baseline + offset, UIScale, color);
            }
        }
    }

    /// <summary>Where a glyph sits off its place at a time, in virtual pixels.</summary>
    private Vector2 Offset(int index, float time)
    {
        switch (_effect)
        {
            case NanoChatEffect.Shake:
                var step = (int) (time * 20);
                return new Vector2(Noise(step * 7919 + index * 31), Noise(step * 104729 + index * 17)) * 1.5f;
            case NanoChatEffect.Wave:
                return new Vector2(0, MathF.Sin(time * 5f - index * 0.5f) * 2.5f);
            default:
                return Vector2.Zero;
        }
    }

    /// <summary>A repeatable value from -1 to 1 for an integer.</summary>
    private static float Noise(int n)
    {
        n = (n << 13) ^ n;
        var hashed = (n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff;
        return 1f - hashed / 1073741824f;
    }
}
