using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Detection;
using Content.Shared._WG.ShipShields;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private readonly Dictionary<EntityUid, WGShipShieldRadarGeometry> _wgRadarShields = new();
    private readonly HashSet<EntityUid> _wgRadarShieldSeen = new();
    private readonly List<EntityUid> _wgRadarShieldRemoved = new();
    private readonly HashSet<EntityUid> _wgRadarShieldGrids = new();

    /// <summary>
    /// Draws detected hull shields on navigation and fire-control maps, including shunted gaps: from the field itself
    /// where this client has it, and from the grid's <see cref="WGShipShieldRadarComponent"/> for ships too far off.
    /// </summary>
    private void DrawWolfgateShields(DrawingHandleScreen handle, TransformComponent consoleXform, Matrix3x2 worldToView)
    {
        _wgRadarShieldSeen.Clear();
        _wgRadarShieldGrids.Clear();
        var shields = EntManager.AllEntityQueryEnumerator<WGShipShieldVisualsComponent, TransformComponent>();
        while (shields.MoveNext(out var uid, out var visuals, out var xform))
        {
            if (xform.MapID != consoleXform.MapID || visuals.Grid is not { } grid ||
                !EntManager.TryGetComponent<PhysicsComponent>(uid, out var physics) || !physics.CanCollide ||
                !CanDrawWolfgateShield(grid, consoleXform, out var gridXform))
                continue;

            _wgRadarShieldGrids.Add(grid);
            EntManager.TryGetComponent<WGShipShieldShuntComponent>(uid, out var allocation);
            var tint = EntManager.TryGetComponent<ShipShieldVisualsComponent>(uid, out var generatorVisuals)
                ? WGShipShieldEffects.HealthColor(visuals.Health, generatorVisuals.ShieldColor)
                : WGShipShieldEffects.HealthColor(visuals.Health);
            DrawWolfgateShieldOutline(handle, uid, visuals.Contours, allocation, WgHardened(grid, tint), gridXform, worldToView);
        }

        var far = EntManager.AllEntityQueryEnumerator<WGShipShieldRadarComponent>();
        while (far.MoveNext(out var grid, out var radar))
        {
            if (_wgRadarShieldGrids.Contains(grid) || !CanDrawWolfgateShield(grid, consoleXform, out var gridXform))
                continue;
            EntManager.TryGetComponent<WGShipShieldShuntComponent>(grid, out var allocation);
            DrawWolfgateShieldOutline(handle, grid, radar.Contours, allocation,
                WgHardened(grid, WGShipShieldEffects.HealthColor(radar.Health, radar.Color)), gridXform, worldToView);
        }
        _wgRadarShieldRemoved.Clear();
        foreach (var uid in _wgRadarShields.Keys)
        {
            if (!_wgRadarShieldSeen.Contains(uid))
                _wgRadarShieldRemoved.Add(uid);
        }
        foreach (var uid in _wgRadarShieldRemoved)
            _wgRadarShields.Remove(uid);
        DrawWolfgateVents(handle, consoleXform, worldToView);
        DrawWolfgateOverloads(handle, consoleXform, worldToView);
    }

    /// <summary>A field's radar colour, turned yellow while its hull hardens it (the Fortress shield module).</summary>
    private Color WgHardened(EntityUid grid, Color tint)
    {
        return EntManager.HasComponent<WGShipShieldHardenedComponent>(grid)
            ? Color.InterpolateBetween(tint, WGShipShieldHardenedComponent.FieldColor, WGShipShieldHardenedComponent.FieldTint)
            : tint;
    }

    /// <summary>Whether a shielded grid shares this radar's map, isn't in FTL and is fully detected.</summary>
    private bool CanDrawWolfgateShield(EntityUid grid, TransformComponent consoleXform, out TransformComponent gridXform)
    {
        return EntManager.TryGetComponent(grid, out gridXform!) && gridXform.MapID == consoleXform.MapID &&
            !EntManager.HasComponent<FTLComponent>(grid) &&
            (_consoleEntity == null || GetGridDetected(grid) == DetectionLevel.Detected);
    }

    /// <summary>Draws one field's outline, posed on its hull, with shunted sectors brighter and open ones gone.</summary>
    private void DrawWolfgateShieldOutline(DrawingHandleScreen handle, EntityUid key, Vector2[][] contours,
        WGShipShieldShuntComponent? allocation, Color tint, TransformComponent gridXform, Matrix3x2 worldToView)
    {
        _wgRadarShieldSeen.Add(key);
        if (!_wgRadarShields.TryGetValue(key, out var outline))
        {
            outline = new WGShipShieldRadarGeometry();
            _wgRadarShields.Add(key, outline);
        }
        outline.Update(contours, allocation?.Center ?? Vector2.Zero,
            allocation?.DirectionRadians ?? 0f, allocation?.Concentration ?? 0f,
            allocation?.ArcRadians ?? MathF.Tau);
        // Match the hull pose instead of the separately replicated collision field.
        var localToView = _transform.GetWorldMatrix(gridXform) * worldToView;
        foreach (var segment in outline.Segments)
        {
            var start = Vector2.Transform(segment.Start, localToView);
            var end = Vector2.Transform(segment.End, localToView);
            // Reject off-screen lines without losing segments crossing the viewport.
            if (MathF.Max(start.X, end.X) < 0f || MathF.Min(start.X, end.X) > PixelSize.X ||
                MathF.Max(start.Y, end.Y) < 0f || MathF.Min(start.Y, end.Y) > PixelSize.Y)
                continue;
            var alpha = Math.Clamp(0.25f + segment.Strength * 0.5f, 0.25f, 1f);
            handle.DrawLine(start, end, tint.WithAlpha(alpha));
        }
    }

    private static readonly Color WgOverloadColor = Color.FromHex("#9FE8FF");
    private const int WgOverloadArcs = 16;
    private const int WgOverloadSparks = 5;

    /// <summary>Draws a broken, flickering ring with sparks round every overloaded hull this radar picks up, even one too far to make out.</summary>
    private void DrawWolfgateOverloads(DrawingHandleScreen handle, TransformComponent consoleXform, Matrix3x2 worldToView)
    {
        var overloads = EntManager.AllEntityQueryEnumerator<WGShipOverloadedComponent, MapGridComponent, TransformComponent>();
        var caption = Loc.GetString("wg-shield-radar-overloaded");
        var time = (float) IoCManager.Resolve<IGameTiming>().RealTime.TotalSeconds;
        var frame = (int) (time * 12f);
        while (overloads.MoveNext(out var grid, out _, out var map, out var xform))
        {
            if (xform.MapID != consoleXform.MapID || EntManager.HasComponent<FTLComponent>(grid))
                continue;
            if (_consoleEntity != null && GetGridDetected(grid) == DetectionLevel.Undetected)
                continue;

            var centre = Vector2.Transform(Vector2.Transform(map.LocalAABB.Center, _transform.GetWorldMatrix(xform)), worldToView);
            var radius = MathF.Max(12f * UIScale, MathF.Max(map.LocalAABB.Width, map.LocalAABB.Height) / 2f * MinimapScale);
            var ring = radius * 1.2f + 4f * UIScale;
            var reach = ring + 9f * UIScale;
            if (centre.X + reach < 0f || centre.X - reach > PixelSize.X || centre.Y + reach < 0f || centre.Y - reach > PixelSize.Y)
                continue;

            var seed = grid.Id * 7919;
            var step = MathF.Tau / WgOverloadArcs;
            for (var i = 0; i < WgOverloadArcs; i++)
            {
                var flicker = WgNoise(seed + i * 31 + frame);
                if (flicker < 0.3f)
                    continue;
                var start = step * (i + 0.15f * WgNoise(seed + i * 17 + frame));
                var end = start + step * 0.6f;
                var colour = WgOverloadColor.WithAlpha(0.35f + 0.6f * flicker);
                var from = centre + WgPolar(start, ring);
                var mid = centre + WgPolar((start + end) / 2f, ring * (0.96f + 0.08f * flicker));
                handle.DrawLine(from, mid, colour);
                handle.DrawLine(mid, centre + WgPolar(end, ring), colour);
            }
            for (var s = 0; s < WgOverloadSparks; s++)
            {
                var strength = WgNoise(seed + s * 97 + frame * 3);
                if (strength < 0.5f)
                    continue;
                var angle = WgNoise(seed + s * 13 + frame) * MathF.Tau;
                var length = (3f + 6f * strength) * UIScale;
                handle.DrawLine(centre + WgPolar(angle, ring), centre + WgPolar(angle + 0.05f, ring + length),
                    Color.White.WithAlpha(strength));
            }

            var size = handle.GetDimensions(Font, caption, UIScale * 0.8f);
            var at = centre - new Vector2(size.X / 2f, ring + size.Y + 3f * UIScale);
            handle.DrawString(Font, at + Vector2.One, caption, UIScale * 0.8f, Color.Black.WithAlpha(0.8f));
            handle.DrawString(Font, at, caption, UIScale * 0.8f, WgOverloadColor);
        }
    }

    private static Vector2 WgPolar(float angle, float distance)
    {
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
    }

    /// <summary>A repeatable pseudo-random value in [0, 1] for an integer, so the flicker is stable within a frame.</summary>
    private static float WgNoise(int n)
    {
        unchecked
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / (float) int.MaxValue;
        }
    }

    private static readonly Color WgVentColor = Color.FromHex("#FF8A1E");

    /// <summary>Draws rings of heat spreading from every venting hull this radar picks up, even one too far to make out.</summary>
    private void DrawWolfgateVents(DrawingHandleScreen handle, TransformComponent consoleXform, Matrix3x2 worldToView)
    {
        var vents = EntManager.AllEntityQueryEnumerator<WGShipVentingComponent, MapGridComponent, TransformComponent>();
        var caption = Loc.GetString("wg-shield-radar-venting");
        var time = (float) IoCManager.Resolve<IGameTiming>().RealTime.TotalSeconds;
        while (vents.MoveNext(out var grid, out _, out var map, out var xform))
        {
            if (xform.MapID != consoleXform.MapID || EntManager.HasComponent<FTLComponent>(grid))
                continue;
            if (_consoleEntity != null && GetGridDetected(grid) == DetectionLevel.Undetected)
                continue;

            var centre = Vector2.Transform(Vector2.Transform(map.LocalAABB.Center, _transform.GetWorldMatrix(xform)), worldToView);
            var radius = MathF.Max(12f * UIScale, MathF.Max(map.LocalAABB.Width, map.LocalAABB.Height) / 2f * MinimapScale);
            var reach = radius * 1.7f + 10f * UIScale;
            if (centre.X + reach < 0f || centre.X - reach > PixelSize.X || centre.Y + reach < 0f || centre.Y - reach > PixelSize.Y)
                continue;

            handle.DrawCircle(centre, radius, WgVentColor.WithAlpha(0.9f), false);
            for (var ring = 0; ring < 3; ring++)
            {
                var phase = (time * 0.6f + ring / 3f) % 1f;
                handle.DrawCircle(centre, radius + (reach - radius) * phase, WgVentColor.WithAlpha(0.8f * (1f - phase)), false);
            }
            var size = handle.GetDimensions(Font, caption, UIScale * 0.8f);
            var at = centre + new Vector2(-size.X / 2f, reach + 2f * UIScale);
            handle.DrawString(Font, at + Vector2.One, caption, UIScale * 0.8f, Color.Black.WithAlpha(0.8f));
            handle.DrawString(Font, at, caption, UIScale * 0.8f, WgVentColor);
        }
    }
}
