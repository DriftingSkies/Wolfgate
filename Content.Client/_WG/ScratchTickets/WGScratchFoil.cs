using System.Numerics;
using Content.Shared._WG.ScratchTickets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WG.ScratchTickets;

/// <summary>The silver layer over a ticket's cells. Dragging across it wears it away.</summary>
public sealed partial class WGScratchFoil : Control
{
    /// <summary>The foil is this many pixels on a side, whatever size it is drawn at.</summary>
    public const int Resolution = 264;

    private const float BrushRadius = 15f;

    // One stamp takes this much opacity at its centre; stamps overlap, so a slow pass clears and a quick one thins.
    private const float BrushStrength = 46f;
    private const float StampSpacing = 4f;

    // A pixel thinner than this counts as scratched off.
    private const byte ClearedBelow = 72;
    private const float FadePerSecond = 900f;

    [Dependency] private IClyde _clyde = default!;

    private readonly Rgba32[] _pixels = new Rgba32[Resolution * Resolution];
    private byte[] _alpha = new byte[Resolution * Resolution];
    private OwnedTexture? _texture;
    private Vector2? _last;
    private int _cleared;
    private bool _dirty = true;
    private bool _fading;
    private float _fade;

    /// <summary>The player dragged across the foil.</summary>
    public event Action? Scratched;

    /// <summary>Enough foil has come off.</summary>
    public event Action? Revealed;

    /// <summary>The share of the foil scratched off, 0 to 1.</summary>
    public float Fraction => _cleared / (float) _alpha.Length;

    public WGScratchFoil()
    {
        IoCManager.InjectDependencies(this);
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.Crosshair;
        Array.Fill(_alpha, byte.MaxValue);
        Repaint();
    }

    /// <summary>Uses <paramref name="alpha"/> as the foil's opacity and keeps writing to it.</summary>
    public void SetAlpha(byte[] alpha)
    {
        if (alpha.Length != _alpha.Length)
            return;

        _alpha = alpha;
        Repaint();
    }

    /// <summary>Fades out whatever foil is left.</summary>
    public void Clear()
    {
        _last = null;
        _fading = Visible;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        _texture = _clyde.CreateBlankTexture<Rgba32>((Resolution, Resolution), "wg-scratch-foil");
        _dirty = true;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _texture?.Dispose();
        _texture = null;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || _fading)
            return;

        args.Handle();
        var point = ToFoil(args.RelativePosition);
        _last = point;
        Scratch(point, point);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function == EngineKeyFunctions.UIClick)
            _last = null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_last is not { } last || _fading)
            return;

        var point = ToFoil(args.RelativePosition);
        Scratch(last, point);
        _last = point;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_fading)
            Fade(args.DeltaSeconds);

        if (!_dirty || _texture == null)
            return;

        _texture.SetSubImage<Rgba32>(Vector2i.Zero, (Resolution, Resolution), _pixels);
        _dirty = false;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_texture != null)
            handle.DrawTextureRect(_texture, PixelSizeBox);
    }

    private Vector2 ToFoil(Vector2 position)
    {
        return Size.X <= 0 || Size.Y <= 0 ? Vector2.Zero : position / Size * Resolution;
    }

    private void Scratch(Vector2 from, Vector2 to)
    {
        var steps = Math.Max(1, (int) MathF.Ceiling((to - from).Length() / StampSpacing));
        for (var i = 1; i <= steps; i++)
        {
            Stamp(Vector2.Lerp(from, to, i / (float) steps));
        }

        _dirty = true;
        Scratched?.Invoke();

        if (Fraction < WGScratchTicketRules.RevealFraction)
            return;

        Clear();
        Revealed?.Invoke();
    }

    private void Stamp(Vector2 centre)
    {
        var x0 = Math.Max(0, (int) (centre.X - BrushRadius));
        var x1 = Math.Min(Resolution - 1, (int) (centre.X + BrushRadius));
        var y0 = Math.Max(0, (int) (centre.Y - BrushRadius));
        var y1 = Math.Min(Resolution - 1, (int) (centre.Y + BrushRadius));

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var dx = x - centre.X;
                var dy = y - centre.Y;
                var falloff = 1f - (dx * dx + dy * dy) / (BrushRadius * BrushRadius);
                if (falloff <= 0)
                    continue;

                var index = y * Resolution + x;
                var before = _alpha[index];
                if (before == 0)
                    continue;

                // Grain: some flecks let go sooner than their neighbours.
                var cut = BrushStrength * falloff * (0.4f + Noise(index) * 1.2f);
                var after = (byte) Math.Max(0f, before - cut);
                if (before >= ClearedBelow && after < ClearedBelow)
                    _cleared++;

                _alpha[index] = after;
                _pixels[index].A = after;
            }
        }
    }

    private void Fade(float seconds)
    {
        _fade += FadePerSecond * seconds;
        var cut = (int) _fade;
        if (cut <= 0)
            return;

        _fade -= cut;
        var left = false;
        for (var i = 0; i < _alpha.Length; i++)
        {
            var after = (byte) Math.Max(0, _alpha[i] - cut);
            _alpha[i] = after;
            _pixels[i].A = after;
            left |= after > 0;
        }

        _cleared = _alpha.Length;
        _dirty = true;

        if (left)
            return;

        _fading = false;
        Visible = false;
    }

    /// <summary>Paints the foil's colours and counts what is already scratched.</summary>
    private void Repaint()
    {
        _cleared = 0;
        for (var y = 0; y < Resolution; y++)
        {
            for (var x = 0; x < Resolution; x++)
            {
                var index = y * Resolution + x;

                // Silver latex: a slanted sheen, fine grain and the printed diamond lattice.
                var band = (x + y * 2) / 44 % 2 == 0 ? 8 : 0;
                var lattice = (x + y) % 24 < 2 || (x - y + Resolution) % 24 < 2 ? 22 : 0;
                var value = (byte) (158 + band + lattice + (int) (Noise(index + 7919) * 14f));
                _pixels[index] = new Rgba32(value, value, (byte) Math.Min(255, value + 8), _alpha[index]);

                if (_alpha[index] < ClearedBelow)
                    _cleared++;
            }
        }

        _dirty = true;
    }

    /// <summary>A fixed pseudo-random value from 0 to 1 per pixel.</summary>
    private static float Noise(int index)
    {
        var hash = (uint) index * 2654435761u;
        hash ^= hash >> 15;
        hash *= 2246822519u;
        hash ^= hash >> 13;
        return (hash & 0xFFFF) / 65535f;
    }
}
