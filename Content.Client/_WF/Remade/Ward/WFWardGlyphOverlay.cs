using System.Numerics;
using Content.Shared._WF.Remade.Ward;
using Content.Shared._WF.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._WF.Remade.Ward;

/// <summary>
/// Faint Marker glyphs drifting through the Ward's field, set in the Shage font where an uppercase letter is a glyph. They
/// ride the hull's outline inside the band, each fading in and out on its own, and stand still with reduced motion. They are
/// drawn over the world in screen space, so they sit in front of the ship's edge.
/// </summary>
public sealed class WFWardGlyphOverlay : Overlay
{
    /// <summary>Point size of the glyph font; the draw scale follows the zoom.</summary>
    public const int FontSize = 16;

    private const int MinGlyphs = 14;
    private const int MaxGlyphs = 30;

    private static readonly Color Ink = Color.FromHex("#c0262a");

    private readonly IEntityManager _entities;
    private readonly IGameTiming _timing;
    private readonly Font _font;
    private readonly SharedTransformSystem _transforms;
    private readonly Dictionary<EntityUid, Trace> _traces = new();
    private readonly List<EntityUid> _expired = new();

    /// <summary>True with reduced motion: the glyphs hold still.</summary>
    public bool Still;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public WFWardGlyphOverlay(IEntityManager entities, IGameTiming timing, Font font)
    {
        _entities = entities;
        _timing = timing;
        _font = font;
        _transforms = entities.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.ViewportControl == null)
            return;

        var toScreen = args.ViewportControl.GetWorldToScreenMatrix();
        var pixelsPerTile = MathF.Sqrt(toScreen.M11 * toScreen.M11 + toScreen.M12 * toScreen.M12);
        if (pixelsPerTile < 12f)
            return;

        _expired.Clear();
        foreach (var uid in _traces.Keys)
        {
            if (_entities.Deleted(uid))
                _expired.Add(uid);
        }

        foreach (var uid in _expired)
        {
            _traces.Remove(uid);
        }

        var handle = args.ScreenHandle;
        var scale = Math.Clamp(pixelsPerTile * 0.55f / FontSize, 0.6f, 5f);
        var time = Still ? 0f : (float) _timing.CurTime.TotalSeconds;
        var view = args.WorldBounds.Enlarged(4f);
        var query = _entities.EntityQueryEnumerator<WFWardFieldComponent, WFShipShieldVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var field, out var visuals, out var xform))
        {
            if (xform.MapID != args.MapId || visuals.Contours.Length == 0)
                continue;

            if (!_traces.TryGetValue(uid, out var trace) || !WFShipShieldMesh.ContoursEqual(trace.Source, visuals.Contours))
            {
                trace = Trace.From(visuals.Contours);
                _traces[uid] = trace;
            }

            if (trace.Length < 1f)
                continue;

            var anchor = xform;
            if (visuals.Grid is { } grid && _entities.TryGetComponent<TransformComponent>(grid, out var gridTransform))
                anchor = gridTransform;

            var matrix = _transforms.GetWorldMatrix(anchor);
            var hardness = WFWardOverlay.Hardness(field);
            var count = MinGlyphs + (int) ((MaxGlyphs - MinGlyphs) * hardness);
            for (var i = 0; i < count; i++)
            {
                // Each glyph creeps round the hull at its own slow pace and floats a little way in from the edge.
                var seed = i * 0.6180339f;
                var along = (i + 0.5f) / count + time * (0.004f + 0.003f * MathF.Sin(i * 1.7f)) + 0.02f * MathF.Sin(time * 0.11f + i);
                var inward = 0.7f + 2.0f * (seed % 1f);
                var local = trace.At(along - MathF.Floor(along), inward);
                var world = Vector2.Transform(local, matrix);
                if (!view.Contains(world))
                    continue;

                // Fades up and down on its own beat, faint to start and clearer as the stone sets.
                var beat = 0.5f + 0.5f * MathF.Sin(time * (0.35f + 0.1f * (seed % 1f)) + i * 2.3f);
                var alpha = (0.07f + 0.16f * hardness) * beat;
                if (alpha < 0.01f)
                    continue;

                var letter = (char) ('A' + (i * 7 + 3) % 26);
                var at = Vector2.Transform(world, toScreen) - new Vector2(FontSize * 0.3f, FontSize * 0.5f) * scale;
                handle.DrawString(_font, at, letter.ToString(), scale, (field.Tint ?? Ink).WithAlpha(alpha));
            }
        }
    }

    /// <summary>The longest contour of a field as a closed path with the distance round it, so a glyph can be placed by a fraction of the way along.</summary>
    private sealed class Trace
    {
        public Vector2[][] Source = Array.Empty<Vector2[]>();
        private Vector2[] _points = Array.Empty<Vector2>();
        private float[] _cumulative = Array.Empty<float>();
        public float Length;

        public static Trace From(Vector2[][] contours)
        {
            var best = contours[0];
            var bestLength = -1f;
            foreach (var contour in contours)
            {
                var length = 0f;
                for (var i = 0; i < contour.Length; i++)
                {
                    length += Vector2.Distance(contour[i], contour[(i + 1) % contour.Length]);
                }

                if (length > bestLength)
                {
                    best = contour;
                    bestLength = length;
                }
            }

            var trace = new Trace { Source = contours, _points = best, Length = Math.Max(bestLength, 0f) };
            trace._cumulative = new float[best.Length + 1];
            for (var i = 0; i < best.Length; i++)
            {
                trace._cumulative[i + 1] = trace._cumulative[i] + Vector2.Distance(best[i], best[(i + 1) % best.Length]);
            }

            return trace;
        }

        /// <summary>The point a fraction of the way round, pushed <paramref name="inward"/> metres into the field (outlines run counterclockwise).</summary>
        public Vector2 At(float fraction, float inward)
        {
            var target = Math.Clamp(fraction, 0f, 0.99999f) * Length;
            // The last point at or before the target distance (Array.BinarySearch isn't in the sandbox whitelist).
            var low = 0;
            var high = _points.Length - 1;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (_cumulative[middle] <= target)
                    low = middle;
                else
                    high = middle - 1;
            }

            var index = low;
            var from = _points[index];
            var to = _points[(index + 1) % _points.Length];
            var span = _cumulative[index + 1] - _cumulative[index];
            var edge = to - from;
            var point = span > 0.0001f ? from + edge * ((target - _cumulative[index]) / span) : from;
            if (edge.LengthSquared() < 0.0001f)
                return point;

            var direction = Vector2.Normalize(edge);
            return point + new Vector2(-direction.Y, direction.X) * inward;
        }
    }
}
