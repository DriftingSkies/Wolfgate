using System;
using System.Numerics;
using Content.Shared._WF.Vehicles.Core;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Client._WF.Vehicles.Overlays;

/// <summary>Debug overlay: a vehicle's doorways outside it, and the landing spots and exit markers on its interior map.</summary>
public sealed class VehicleEntryPointsOverlay : Overlay
{
    private static readonly Color[] EntryColors =
    {
        Color.Red,
        Color.Green,
        Color.Blue,
        Color.Yellow,
        Color.Magenta,
        Color.Cyan,
        Color.Orange,
    };

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    private readonly IEntityManager _ents;
    private readonly SharedTransformSystem _transform;

    public VehicleEntryPointsOverlay(IEntityManager ents)
    {
        _ents = ents;
        _transform = ents.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        DrawDoorways(args.WorldHandle, args.MapId);
        DrawInterior(args.WorldHandle, args.MapId);
    }

    private void DrawDoorways(DrawingHandleWorld handle, MapId mapId)
    {
        var query = _ents.EntityQueryEnumerator<VehicleEnterComponent, TransformComponent>();
        while (query.MoveNext(out _, out var enter, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            var (position, rotation) = _transform.GetWorldPositionRotation(xform);

            for (var i = 0; i < enter.EntryPoints.Count; i++)
            {
                var point = enter.EntryPoints[i];
                DrawPoint(handle, position + rotation.RotateVec(point.Offset), Math.Max(0.05f, point.Radius), GetColor(i));
            }
        }
    }

    private void DrawInterior(DrawingHandleWorld handle, MapId mapId)
    {
        var links = _ents.EntityQueryEnumerator<VehicleInteriorLinkComponent, TransformComponent>();
        while (links.MoveNext(out _, out var link, out var linkXform))
        {
            if (linkXform.MapID != mapId ||
                !_ents.TryGetComponent(link.Vehicle, out VehicleEnterComponent? enter))
            {
                continue;
            }

            if (FindGrid(mapId) is not { } grid)
                continue;

            var matrix = _transform.GetWorldMatrix(grid);
            for (var i = 0; i < enter.EntryPoints.Count; i++)
            {
                if (enter.EntryPoints[i].InteriorCoords is { } landing)
                    DrawPoint(handle, Vector2.Transform(landing, matrix), 0.15f, GetColor(i));
            }
        }

        var exits = _ents.EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
        while (exits.MoveNext(out _, out var exit, out var exitXform))
        {
            if (exitXform.MapID != mapId)
                continue;

            DrawPoint(handle, _transform.GetWorldPosition(exitXform), 0.12f, GetColor(exit.EntryIndex));
        }
    }

    private EntityUid? FindGrid(MapId mapId)
    {
        var query = _ents.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == mapId)
                return uid;
        }

        return null;
    }

    private static Color GetColor(int index)
    {
        return EntryColors[Math.Abs(index) % EntryColors.Length];
    }

    private static void DrawPoint(DrawingHandleWorld handle, Vector2 position, float radius, Color color)
    {
        handle.DrawCircle(position, radius, color.WithAlpha(0.22f), true);
        handle.DrawCircle(position, radius, color.WithAlpha(0.85f), false);
    }
}
