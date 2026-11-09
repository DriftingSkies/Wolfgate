using System.Numerics;
using Content.Client._WF.Lattice;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Content.Shared._WF.Lattice;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private static readonly Color WfPhantomColor = Color.FromHex("#c8c8c8");

    /// <summary>A contact's radar label, garbled if it or the viewer is out past the Lattice's hold.</summary>
    private string? WfLatticeGarble(string? label, EntityUid grid)
    {
        if (label == null)
            return null;

        var map = EntManager.GetComponent<TransformComponent>(grid).MapUid;
        return EntManager.System<WFLatticeViewSystem>().Garble(label, map, _transform.GetWorldPosition(grid), grid.Id);
    }

    private static readonly Color WfDeadZoneColor = Color.FromHex("#07020a").WithAlpha(0.93f);
    private static readonly Color WfDeadZoneStatic = Color.FromHex("#5a1030").WithAlpha(0.5f);

    private const int WfDeadZoneSteps = 16;
    private readonly Vector2[] _wfDeadZoneFan = new Vector2[WfDeadZoneSteps + 2];

    /// <summary>Seconds each radar dead zone lasts before it moves.</summary>
    private const float WfDeadZoneSeconds = 25f;

    private static readonly Color WfStaticColor = Color.FromHex("#b8a8c8");
    private static readonly Color WfStaticBack = Color.FromHex("#0b0510");

    /// <summary>Pixels per side of the cells the scope is sampled in to find where the Coda is.</summary>
    private const float WfStaticCell = 14f;

    /// <summary>Static specks per thousand square pixels of Coda on the scope.</summary>
    private const float WfStaticDensity = 1.6f;

    /// <summary>Seconds between bursts of heavier snow across the Coda's static.</summary>
    private const float WfSnowEvery = 6f;
    private const float WfSnowSeconds = 0.5f;

    private readonly System.Random _wfStatic = new();
    private readonly List<(Vector2 Position, float Strength)> _wfAnchors = new();
    private bool[] _wfCoda = Array.Empty<bool>();

    /// <summary>
    /// Wherever the scope shows the Coda, it shows only static: nothing past the Lattice's last edge reads on any
    /// scanner, and from inside the Coda the whole scope is static. The edge is found by sampling the field across the
    /// scope, so it follows the band wherever lost anchors have pulled it. The Choir see straight through it.
    /// </summary>
    private void DrawWfLatticeStatic(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map, float time)
    {
        if (map is not { } mapUid
            || !EntManager.TryGetComponent<WFLatticeFieldComponent>(mapUid, out var field)
            || !IoCManager.Resolve<IPrototypeManager>().TryIndex(field.Profile, out var profile)
            || field.Thresholds.Count < profile.HazardBand
            || EntManager.System<WFLatticeViewSystem>().SeesThrough()
            || !Matrix3x2.Invert(worldToView, out var viewToWorld))
            return;

        _wfAnchors.Clear();
        for (var i = 0; i < field.Anchors.Count && i < field.Strengths.Count; i++)
        {
            _wfAnchors.Add((field.Anchors[i], field.Strengths[i]));
        }

        var size = PixelSize;
        var cell = WfStaticCell * UIScale;
        var columns = (int) MathF.Ceiling(size.X / cell);
        var rows = (int) MathF.Ceiling(size.Y / cell);
        if (_wfCoda.Length < columns * rows)
            _wfCoda = new bool[columns * rows];

        var codaCells = 0;
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                var view = new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);
                var world = Vector2.Transform(view, viewToWorld);
                var inCoda = WFLatticeMath.Band(WFLatticeMath.Field(world, _wfAnchors, profile.Reach), field.Thresholds) >= profile.HazardBand;
                _wfCoda[y * columns + x] = inCoda;
                if (!inCoda)
                    continue;

                codaCells++;
                var from = new Vector2(x * cell, y * cell);
                handle.DrawRect(new UIBox2(from, Vector2.Min(from + new Vector2(cell, cell), size)), WfStaticBack.WithAlpha(0.94f));
            }
        }

        if (codaCells == 0)
            return;

        // Reduced motion: the pattern shifts twice a second instead of every frame, and there are no bursts.
        var still = IoCManager.Resolve<IConfigurationManager>().GetCVar(CCVars.ReducedMotion);
        var random = still ? new System.Random((int) (time * 2f)) : _wfStatic;
        var burst = !still && time % WfSnowEvery < WfSnowSeconds;
        // Specks are scattered over the whole scope and kept only where it shows the Coda, so this gives the density there.
        var specks = (int) (size.X * size.Y / 1000f * WfStaticDensity * (burst ? 2.5f : 1f));
        for (var i = 0; i < specks; i++)
        {
            var at = new Vector2((float) random.NextDouble() * size.X, (float) random.NextDouble() * size.Y);
            var cx = Math.Min(columns - 1, (int) (at.X / cell));
            var cy = Math.Min(rows - 1, (int) (at.Y / cell));
            if (!_wfCoda[cy * columns + cx])
                continue;

            var length = (2f + (float) random.NextDouble() * 6f) * UIScale;
            var alpha = (burst ? 0.45f : 0.25f) + (float) random.NextDouble() * 0.4f;
            handle.DrawRect(new UIBox2(at, at + new Vector2(length, 2f * UIScale)), WfStaticColor.WithAlpha(alpha));
        }

        // Torn scanlines through the static, and a band of interference rolling down it.
        for (var i = 0; i < 6; i++)
        {
            var y = (float) random.NextDouble() * size.Y;
            DrawWfStaticRow(handle, y, UIScale, columns, cell, WfStaticColor.WithAlpha(0.2f));
        }

        var band = (time * 0.35f % 1f) * (size.Y + 60f * UIScale) - 30f * UIScale;
        for (var y = band; y < band + 28f * UIScale; y += 4f * UIScale)
        {
            DrawWfStaticRow(handle, y, 4f * UIScale, columns, cell, WfStaticColor.WithAlpha(0.12f));
        }
    }

    /// <summary>Draws a strip of static across one row of the scope, only where it shows the Coda.</summary>
    private void DrawWfStaticRow(DrawingHandleScreen handle, float y, float height, int columns, float cell, Color colour)
    {
        if (y < 0f || y >= PixelSize.Y)
            return;

        var row = Math.Min((int) (y / cell), (int) MathF.Ceiling(PixelSize.Y / cell) - 1);
        var runStart = -1;
        for (var x = 0; x <= columns; x++)
        {
            var inCoda = x < columns && _wfCoda[row * columns + x];
            if (inCoda && runStart < 0)
                runStart = x;

            if (inCoda || runStart < 0)
                continue;

            handle.DrawRect(new UIBox2(runStart * cell, y, Math.Min(x * cell, PixelSize.X), y + height), colour);
            runStart = -1;
        }
    }

    /// <summary>
    /// In the Coda the radar goes blind in wedges that wander round the scope; and wherever the scope shows the Coda,
    /// from inside it or out, it shows only static.
    /// </summary>
    private void DrawWfLatticeDeadZones(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        var time = (float) IoCManager.Resolve<IGameTiming>().RealTime.TotalSeconds;
        DrawWfLatticeStatic(handle, worldToView, map, time);
        if (!EntManager.System<WFLatticeViewSystem>().InHazardBand())
            return;

        var dark = WfDeadZoneColor;
        var noise = WfDeadZoneStatic;
        var reach = PixelSize.Length;
        var centre = MidPointVector;
        for (var zone = 0; zone < 2; zone++)
        {
            var phase = time / WfDeadZoneSeconds + zone * 0.5f;
            var cycle = (int) MathF.Floor(phase);
            var t = phase - cycle;
            var fade = Math.Clamp(MathF.Min(t, 1f - t) * WfDeadZoneSeconds / 3f, 0f, 1f);
            if (fade <= 0f)
                continue;

            var seed = (uint) (cycle * 7919 + zone * 104729) * 2654435761u;
            var start = (seed % 3600) / 10f * MathF.PI / 180f + time * 0.05f;
            var width = (35f + (seed >> 12) % 45) * MathF.PI / 180f;
            const int steps = WfDeadZoneSteps;
            var fan = _wfDeadZoneFan;
            fan[0] = centre;
            for (var i = 0; i <= steps; i++)
            {
                var angle = start + width * i / steps;
                fan[i + 1] = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * reach;
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, fan, dark.WithAlpha(dark.A * fade));

            // A few lines of static crawling through it.
            for (var line = 0; line < 6; line++)
            {
                var angle = start + width * (((seed >> (line * 3)) % 100) / 100f + MathF.Sin(time * 3f + line) * 0.02f);
                var from = 20f + ((seed >> line) % 60) * UIScale;
                var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                handle.DrawLine(centre + dir * from, centre + dir * (from + 40f * UIScale), noise.WithAlpha(noise.A * fade));
            }
        }
    }

    /// <summary>Radar contacts that only this player sees.</summary>
    private void DrawWfLatticePhantoms(DrawingHandleScreen handle, Matrix3x2 worldToView, EntityUid? map)
    {
        if (map is not { } mapUid)
            return;

        var mapId = EntManager.GetComponent<TransformComponent>(mapUid).MapID;
        var view = EntManager.System<WFLatticeViewSystem>();
        foreach (var (position, _, seed) in EntManager.System<WFLatticeHallucinationSystem>().Blips)
        {
            if (position.MapId != mapId)
                continue;

            var at = Vector2.Transform(position.Position, worldToView);
            if (at.X < 0f || at.Y < 0f || at.X > PixelSize.X || at.Y > PixelSize.Y)
                continue;

            handle.DrawCircle(at, 3f * UIScale, WfPhantomColor);
            var label = view.Garble(Loc.GetString("wf-lattice-phantom-contact"), mapUid, position.Position, seed);
            handle.DrawString(Font, at + new Vector2(6f, -6f) * UIScale, label, UIScale * 0.8f, WfPhantomColor);
        }
    }
}
