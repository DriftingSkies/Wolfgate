using System.Numerics;
using Content.Shared._WG.ShipModules;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Client._WG.ShipModules;

/// <summary>
/// Draws ship module effects in the world: violet ghosts of a skimming hull along its path and a shimmer where it
/// lands, and cyan rings spinning off guns swung by gyros.
/// </summary>
public sealed partial class WGShipModuleFxOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly Color PhaseColor = Color.FromHex("#B48CFF");
    private static readonly Color GyroColor = Color.FromHex("#6FE7FF");

    /// <summary>Ghosts drawn between where a skim left and where it landed.</summary>
    private const int Ghosts = 4;

    /// <summary>Most tiles drawn per ghost, so a capital's skim stays cheap.</summary>
    private const int MaxTiles = 2500;

    private readonly WGShipModuleFxSystem _fx;
    private readonly SharedTransformSystem _transform;
    private readonly SharedMapSystem _map;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WGShipModuleFxOverlay(WGShipModuleFxSystem fx)
    {
        IoCManager.InjectDependencies(this);
        _fx = fx;
        _transform = _entities.System<SharedTransformSystem>();
        _map = _entities.System<SharedMapSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var now = _timing.RealTime;
        foreach (var skim in _fx.Skims)
        {
            if (skim.Map == args.MapId)
                DrawSkim(handle, skim, now);
        }

        handle.SetTransform(Matrix3x2.Identity);
        foreach (var (gun, start) in _fx.Swings)
        {
            if (!TryPosition(gun, args.MapId, out var position))
                continue;

            var age = (float) ((now - start).TotalSeconds / WGShipModuleFxSystem.SwingLife.TotalSeconds);
            var alpha = 1f - age;
            handle.DrawCircle(position, 0.6f + age * 1.2f, GyroColor.WithAlpha(0.8f * alpha), false);
            handle.DrawCircle(position, 0.4f + age * 0.6f, GyroColor.WithAlpha(0.5f * alpha), false);
        }
    }

    private bool TryPosition(EntityUid uid, MapId map, out Vector2 position)
    {
        position = default;
        if (!_entities.TryGetComponent(uid, out TransformComponent? xform) || xform.MapID != map)
            return false;

        position = _transform.GetWorldPosition(xform);
        return true;
    }

    /// <summary>Ghosts of the hull fading out along its path, and a shimmer over it where it landed.</summary>
    private void DrawSkim(DrawingHandleWorld handle, WGShipModuleFxSystem.Skim skim, TimeSpan now)
    {
        if (!_entities.TryGetComponent(skim.Ship, out MapGridComponent? grid)
            || !_entities.TryGetComponent(skim.Ship, out TransformComponent? xform))
            return;

        var age = (float) ((now - skim.Start).TotalSeconds / WGShipModuleFxSystem.Skim.Life.TotalSeconds);
        var fade = Math.Clamp(1f - age, 0f, 1f);
        for (var i = 0; i < Ghosts; i++)
        {
            var along = i / (float) Ghosts;
            var position = Vector2.Lerp(skim.From, skim.To, along);
            // The ghost nearest where it left is the brightest and fades first.
            var alpha = fade * (0.45f - 0.08f * i);
            DrawHull(handle, skim.Ship, grid, position, skim.FromRotation, PhaseColor.WithAlpha(alpha));
        }

        // The hull itself shimmers back in.
        var (here, rotation) = _transform.GetWorldPositionRotation(xform);
        var flicker = 0.25f * fade * (0.6f + 0.4f * MathF.Sin(age * 40f));
        DrawHull(handle, skim.Ship, grid, here, rotation, PhaseColor.WithAlpha(flicker));
    }

    private void DrawHull(DrawingHandleWorld handle, EntityUid uid, MapGridComponent grid, Vector2 position, Angle rotation,
        Color color)
    {
        if (color.A <= 0.01f)
            return;

        handle.SetTransform(Matrix3Helpers.CreateTransform(position, rotation));
        var size = grid.TileSize;
        var drawn = 0;
        foreach (var tile in _map.GetAllTiles(uid, grid))
        {
            if (drawn++ >= MaxTiles)
                break;

            var origin = (Vector2) tile.GridIndices * size;
            handle.DrawRect(new Box2(origin, origin + new Vector2(size, size)), color);
        }
    }
}
