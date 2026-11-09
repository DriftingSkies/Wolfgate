using System.Numerics;
using Content.Shared._WF.Lattice;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Lattice;

/// <summary>
/// The Lattice status display: the hex of anchors drawn as glyph nodes and flowing beams, Caelestinus Central's anchor at
/// its heart, and readouts for the field's integrity, the hex, the flesh ships and the band the viewer is in. It reads the
/// networked field on its viewer's map, so the PDA app and the console share it.
/// </summary>
public sealed class WFLatticeStatusPanel : Control
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private static readonly Color Back = Color.FromHex("#07020a");
    private static readonly Color Grid = Color.FromHex("#2a0f3a");
    private static readonly Color Violet = Color.FromHex("#c060ff");
    private static readonly Color VioletDim = Color.FromHex("#6a2a8a");
    private static readonly Color Pale = Color.FromHex("#e8d0ff");
    private static readonly Color Red = Color.FromHex("#d03040");
    private static readonly Color Grey = Color.FromHex("#5a5060");
    private static readonly Color Flesh = Color.FromHex("#c04a6a");

    private const string Glyphs = "ΔΞΨΩΣΦΘΛЖЯЮБДФЦЩ";

    private readonly Font _title;
    private readonly Font _text;
    private readonly Font _small;
    private readonly Vector2[] _circle = new Vector2[33];

    /// <summary>Whose map the display reads: the PDA or the console.</summary>
    public EntityUid? Viewer;

    public WFLatticeStatusPanel()
    {
        IoCManager.InjectDependencies(this);
        RectClipContent = true;
        MinSize = new Vector2(340, 420);
        var cache = IoCManager.Resolve<IResourceCache>();
        _title = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Bold.ttf"), 13);
        _text = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 11);
        _small = new VectorFont(cache.GetResource<FontResource>("/Fonts/RobotoMono/RobotoMono-Regular.ttf"), 9);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var size = PixelSize;
        var time = (float) _timing.RealTime.TotalSeconds;
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, size), Back);
        DrawBackdrop(handle, size, time);

        var field = Field(out var viewerAt);
        var pad = 12f * UIScale;
        var y = pad;
        handle.DrawString(_title, new Vector2(pad, y), Loc.GetString("wf-lattice-status-title"), Pale);
        y += 18f * UIScale;
        handle.DrawString(_small, new Vector2(pad, y), Garbled(28, time), VioletDim);
        y += 16f * UIScale;

        if (field == null)
        {
            handle.DrawString(_text, new Vector2(pad, y + 20f * UIScale), Loc.GetString("wf-lattice-status-no-signal"), Red);
            return;
        }

        if (field.Tacet)
        {
            DrawTacet(handle, size, time);
            return;
        }

        // Ring beside the readouts when wide, above them when tall.
        var wide = size.X > size.Y * 1.25f;
        var ringSize = wide ? MathF.Min(size.X * 0.55f, size.Y - y - pad) : MathF.Min(size.X - pad * 2f, (size.Y - y) * 0.5f);
        var ringBox = UIBox2.FromDimensions(new Vector2(pad, y), new Vector2(ringSize, ringSize));
        DrawRing(handle, field, ringBox, time);

        var textAt = wide ? new Vector2(ringBox.Right + pad, y) : new Vector2(pad, ringBox.Bottom + pad);
        DrawReadouts(handle, field, viewerAt, textAt, time);
    }

    /// <summary>A faint dot grid and a slow scan line.</summary>
    private void DrawBackdrop(DrawingHandleScreen handle, Vector2 size, float time)
    {
        var step = 18f * UIScale;
        for (var x = step / 2f; x < size.X; x += step)
        {
            for (var y = step / 2f; y < size.Y; y += step)
            {
                handle.DrawRect(UIBox2.FromDimensions(new Vector2(x, y), new Vector2(1f, 1f) * UIScale), Grid);
            }
        }

        var scan = (time * 40f * UIScale) % (size.Y + 40f) - 20f;
        handle.DrawRect(UIBox2.FromDimensions(new Vector2(0f, scan), new Vector2(size.X, 2f * UIScale)), Violet.WithAlpha(0.08f));
    }

    private WFLatticeFieldComponent? Field(out Vector2? viewerAt)
    {
        viewerAt = null;
        if (Viewer is not { } viewer || !_entities.TryGetComponent<TransformComponent>(viewer, out var xform)
            || xform.MapUid is not { } map || !_entities.TryGetComponent<WFLatticeFieldComponent>(map, out var field))
            return null;

        viewerAt = _entities.System<SharedTransformSystem>().GetWorldPosition(xform);
        return field;
    }

    /// <summary>The hex: a node per anchor, beams between them, and Caelestinus Central's anchor at the heart.</summary>
    private void DrawRing(DrawingHandleScreen handle, WFLatticeFieldComponent field, UIBox2 box, float time)
    {
        var centre = box.Center;
        var corners = field.Hex;
        var radius = box.Width * 0.38f;
        handle.DrawCircle(centre, box.Width * 0.47f, VioletDim.WithAlpha(0.35f), false);

        if (corners.Count < 3 || field.Unstrung)
        {
            // Nothing holds: the ring is gone, only drifting glyphs where it was.
            for (var i = 0; i < 6; i++)
            {
                var angle = MathF.Tau * i / 6f + time * 0.05f;
                var at = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * (1f + 0.15f * MathF.Sin(time + i));
                handle.DrawString(_text, at, Glyphs[(i * 5 + (int) time) % Glyphs.Length].ToString(), Grey);
            }

            var label = Loc.GetString("wf-lattice-status-unstrung");
            handle.DrawString(_title, centre - new Vector2(label.Length * 4f * UIScale, 8f * UIScale), label, Red);
            return;
        }

        var reach = 1f;
        foreach (var corner in corners)
        {
            reach = MathF.Max(reach, corner.Length());
        }

        // World north up: flip y for the screen.
        Vector2 Place(Vector2 world) => centre + new Vector2(world.X, -world.Y) / reach * radius;

        if (WFLatticeHex.Sealed(corners, field.HexUp))
        {
            var pulse = 0.05f + 0.03f * MathF.Sin(time * 1.5f);
            for (var i = 0; i < corners.Count; i++)
            {
                var a = Place(corners[i]);
                var b = Place(corners[(i + 1) % corners.Count]);
                handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, new[] { centre, a, b }, Violet.WithAlpha(pulse));
            }
        }

        for (var i = 0; i < corners.Count; i++)
        {
            var a = Place(corners[i]);
            var b = Place(corners[(i + 1) % corners.Count]);
            if (i < field.HexUp.Count && field.HexUp[i])
            {
                handle.DrawLine(a, b, Violet);
                // Comets running along each standing beam.
                for (var c = 0; c < 3; c++)
                {
                    var t = (time * 0.35f + c / 3f + i * 0.13f) % 1f;
                    var at = Vector2.Lerp(a, b, t);
                    handle.DrawCircle(at, 2.2f * UIScale, Pale, true);
                }

                continue;
            }

            // A torn beam: jittering red dashes.
            var length = (b - a).Length();
            var step = (b - a) / MathF.Max(length, 1f);
            var normal = new Vector2(-step.Y, step.X);
            for (var along = 0f; along < length; along += 9f * UIScale)
            {
                var jitter = normal * MathF.Sin(along * 0.7f + time * 9f) * 2f * UIScale;
                handle.DrawLine(a + step * along + jitter, a + step * MathF.Min(along + 4f * UIScale, length) + jitter, Red);
            }
        }

        for (var i = 0; i < corners.Count; i++)
        {
            var at = Place(corners[i]);
            var lost = i >= field.HexAlive.Count || !field.HexAlive[i];
            DrawNode(handle, at, lost ? Grey : Violet, lost, time + i);
            handle.DrawString(_small, at + new Vector2(-4f, -22f) * UIScale, Glyphs[i * 3 % Glyphs.Length].ToString(), lost ? Grey : Pale);
        }

        DrawNode(handle, centre, field.CentralStanding ? Pale : Red, !field.CentralStanding, time * 1.3f, 0.7f);

        if (field.FleshShips > 0)
        {
            // Flesh ships as red motes circling outside the ring.
            for (var i = 0; i < field.FleshShips; i++)
            {
                var angle = time * 0.3f + MathF.Tau * i / field.FleshShips;
                var at = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * box.Width * 0.47f;
                handle.DrawCircle(at, 3f * UIScale, Flesh, true);
            }
        }
    }

    /// <summary>An anchor glyph: a ring with a pulsing core, or a grey ring crossed out.</summary>
    private void DrawNode(DrawingHandleScreen handle, Vector2 at, Color color, bool lost, float phase, float scale = 1f)
    {
        var r = 9f * UIScale * scale;
        handle.DrawCircle(at, r, Back, true);
        handle.DrawCircle(at, r, color, false);
        if (lost)
        {
            var d = r * 0.7f;
            handle.DrawLine(at - new Vector2(d, d), at + new Vector2(d, d), Red);
            handle.DrawLine(at + new Vector2(-d, d), at + new Vector2(d, -d), Red);
            return;
        }

        var glow = 0.5f + 0.5f * MathF.Sin(phase * 2.2f);
        handle.DrawCircle(at, r * (0.35f + 0.15f * glow), color.WithAlpha(0.6f + 0.4f * glow), true);
        handle.DrawCircle(at, r * (1.4f + 0.3f * glow), color.WithAlpha(0.25f * (1f - glow)), false);
    }

    private void DrawReadouts(DrawingHandleScreen handle, WFLatticeFieldComponent field, Vector2? viewerAt, Vector2 at, float time)
    {
        var line = 16f * UIScale;
        var y = at.Y;
        void Row(string label, string value, Color color)
        {
            handle.DrawString(_small, new Vector2(at.X, y), label, VioletDim);
            handle.DrawString(_text, new Vector2(at.X, y + 11f * UIScale), value, color);
            y += line * 1.9f;
        }

        var total = Math.Max(field.Hex.Count, 6);
        var state = field.Unstrung ? "wf-lattice-status-state-unstrung" : field.Integrity < 1f ? "wf-lattice-status-state-failing" : "wf-lattice-status-state-holding";
        Row(Loc.GetString("wf-lattice-status-state"), Loc.GetString(state), field.Unstrung ? Red : field.Integrity < 1f ? Flesh : Violet);
        Row(Loc.GetString("wf-lattice-status-integrity"), Bar(field.Unstrung ? 0 : (int) MathF.Round(field.Integrity * total), total), field.Integrity < 0.5f ? Red : Violet);

        var sealedHex = !field.Unstrung && WFLatticeHex.Sealed(field.Hex, field.HexUp);
        var broken = 0;
        foreach (var up in field.HexUp)
        {
            if (!up)
                broken++;
        }

        Row(Loc.GetString("wf-lattice-status-hex"),
            sealedHex ? Loc.GetString("wf-lattice-status-hex-sealed") : Loc.GetString("wf-lattice-status-hex-broken", ("count", field.Unstrung ? total : broken)),
            sealedHex ? Violet : Red);
        Row(Loc.GetString("wf-lattice-status-central"),
            Loc.GetString(field.CentralStanding ? "wf-lattice-status-central-holding" : "wf-lattice-status-central-lost"),
            field.CentralStanding ? Violet : Red);
        Row(Loc.GetString("wf-lattice-status-flesh"),
            field.FleshShips > 0 ? Loc.GetString("wf-lattice-status-flesh-count", ("count", field.FleshShips)) : Loc.GetString("wf-lattice-status-flesh-none"),
            field.FleshShips > 0 ? Flesh : VioletDim);

        if (viewerAt is { } here)
            Row(Loc.GetString("wf-lattice-status-band"), BandName(field, here, time), Pale);

        // The anchors by name, each live or lost.
        for (var i = 0; i < field.HexNames.Count && i < field.HexAlive.Count; i++)
        {
            var alive = field.HexAlive[i];
            var mark = alive && !field.Unstrung ? "Φ" : "x";
            handle.DrawString(_small, new Vector2(at.X, y), $"{mark} {field.HexNames[i]}", alive && !field.Unstrung ? Violet : Grey);
            y += 12f * UIScale;
        }
    }

    /// <summary>The band at the viewer's place, garbled like the band itself if it garbles.</summary>
    private string BandName(WFLatticeFieldComponent field, Vector2 here, float time)
    {
        if (!_proto.TryIndex(field.Profile, out var profile) || profile.Bands.Count == 0)
            return "?";

        var anchors = new List<(Vector2, float)>();
        for (var i = 0; i < field.Anchors.Count && i < field.Strengths.Count; i++)
        {
            anchors.Add((field.Anchors[i], field.Strengths[i]));
        }

        var strength = WFLatticeMath.Field(here, anchors, profile.Reach);
        var index = Math.Min(WFLatticeMath.Band(strength, field.Thresholds), profile.Bands.Count - 1);
        var band = profile.Bands[index];
        var name = Loc.GetString(band.Name);
        return band.Garbled ? WFLatticeMath.Garble(name, 0.6f, (int) (time * 4f)) : name;
    }

    /// <summary>Tacet: nothing left to show but the dark.</summary>
    private void DrawTacet(DrawingHandleScreen handle, Vector2 size, float time)
    {
        handle.DrawRect(UIBox2.FromDimensions(Vector2.Zero, size), Color.Black.WithAlpha(0.85f));
        var label = Loc.GetString("wf-lattice-status-tacet");
        var flicker = 0.3f + 0.2f * MathF.Sin(time * 0.7f);
        handle.DrawString(_title, size / 2f - new Vector2(label.Length * 4f * UIScale, 8f * UIScale), label, Grey.WithAlpha(flicker));
    }

    private static string Bar(int filled, int total)
    {
        var bar = new System.Text.StringBuilder();
        for (var i = 0; i < total; i++)
        {
            bar.Append(i < filled ? '|' : '.');
        }

        bar.Append($" {filled}/{total}");
        return bar.ToString();
    }

    /// <summary>A line of shifting glyphs, the Lattice's own script.</summary>
    private static string Garbled(int length, float time)
    {
        var text = new System.Text.StringBuilder(length);
        var seed = (int) (time * 3f);
        for (var i = 0; i < length; i++)
        {
            text.Append(i % 6 == 5 ? ' ' : Glyphs[(i * 7 + seed * (i % 3 + 1)) % Glyphs.Length]);
        }

        return text.ToString();
    }
}
