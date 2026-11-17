using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Detection;
using Content.Shared._WG.ShipShields;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Shared.Physics.Components;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private readonly Dictionary<EntityUid, WGShipShieldRadarGeometry> _wgRadarShields = new();
    private readonly HashSet<EntityUid> _wgRadarShieldSeen = new();
    private readonly List<EntityUid> _wgRadarShieldRemoved = new();

    /// <summary>Draws detected hull shields on navigation and fire-control maps, including shunted gaps.</summary>
    private void DrawWolfgateShields(DrawingHandleScreen handle, TransformComponent consoleXform, Matrix3x2 worldToView)
    {
        _wgRadarShieldSeen.Clear();
        var shields = EntManager.AllEntityQueryEnumerator<WGShipShieldVisualsComponent, TransformComponent>();
        while (shields.MoveNext(out var uid, out var visuals, out var xform))
        {
            if (xform.MapID != consoleXform.MapID || visuals.Grid is not { } grid ||
                !EntManager.TryGetComponent<PhysicsComponent>(uid, out var physics) || !physics.CanCollide ||
                !EntManager.TryGetComponent<TransformComponent>(grid, out var gridXform) ||
                gridXform.MapID != consoleXform.MapID || EntManager.HasComponent<FTLComponent>(grid))
                continue;
            if (_consoleEntity != null && GetGridDetected(grid) != DetectionLevel.Detected)
                continue;

            _wgRadarShieldSeen.Add(uid);
            if (!_wgRadarShields.TryGetValue(uid, out var outline))
            {
                outline = new WGShipShieldRadarGeometry();
                _wgRadarShields.Add(uid, outline);
            }
            EntManager.TryGetComponent<WGShipShieldShuntComponent>(uid, out var allocation);
            outline.Update(visuals.Contours, allocation?.Center ?? Vector2.Zero,
                allocation?.DirectionRadians ?? 0f, allocation?.Concentration ?? 0f,
                allocation?.ArcRadians ?? MathF.Tau);
            // Match the hull pose instead of the separately replicated collision field.
            var localToView = _transform.GetWorldMatrix(gridXform) * worldToView;
            var tint = EntManager.TryGetComponent<ShipShieldVisualsComponent>(uid, out var generatorVisuals)
                ? WGShipShieldEffects.HealthColor(visuals.Health, generatorVisuals.ShieldColor)
                : WGShipShieldEffects.HealthColor(visuals.Health);
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
        _wgRadarShieldRemoved.Clear();
        foreach (var uid in _wgRadarShields.Keys)
        {
            if (!_wgRadarShieldSeen.Contains(uid))
                _wgRadarShieldRemoved.Add(uid);
        }
        foreach (var uid in _wgRadarShieldRemoved)
            _wgRadarShields.Remove(uid);
    }
}
