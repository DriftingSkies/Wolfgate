using System.Numerics;
using Content.Client.Light;
using Content.Shared._WF.Xenoborgs;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Xenoborgs;

/// <summary>
/// Draws glowing tiles: their glow texture added unlit under entities, and their light added to the light target so
/// the tile and whatever stands on it are lit.
/// </summary>
public sealed class WFTileGlowOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> AdditiveShader = "WFAdditiveUnshaded";

    private readonly IOverlayManager _overlays;
    private readonly IGameTiming _timing;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _transform;
    private readonly EntityLookupSystem _lookup;
    private readonly WFTileGlowSystem _glows;
    private readonly ShaderInstance _additive;
    private List<Entity<MapGridComponent>> _grids = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities | OverlaySpace.BeforeLighting;

    public WFTileGlowOverlay(IEntityManager entities, IOverlayManager overlays, IPrototypeManager prototypes,
        IGameTiming timing, WFTileGlowSystem glows)
    {
        _overlays = overlays;
        _timing = timing;
        _map = entities.System<SharedMapSystem>();
        _transform = entities.System<SharedTransformSystem>();
        _lookup = entities.System<EntityLookupSystem>();
        _glows = glows;
        _additive = prototypes.Index(AdditiveShader).Instance();
        // Between the light target copy and its blur, like tile emission.
        ZIndex = TileEmissionOverlay.ContentZIndex;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.MapId == MapId.Nullspace || _glows.Glows.Count == 0)
            return;

        if (args.Space == OverlaySpace.BeforeLighting)
            DrawLight(args);
        else
            DrawGlow(args);
    }

    private void DrawGlow(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var time = (float) _timing.RealTime.TotalSeconds;
        handle.UseShader(_additive);
        _grids.Clear();
        _map.FindGridsIntersecting(args.MapId, args.WorldAABB, ref _grids, approx: true);
        foreach (var grid in _grids)
        {
            handle.SetTransform(_transform.GetWorldMatrix(grid.Owner));
            var tiles = _map.GetTilesIntersecting(grid.Owner, grid, args.WorldAABB);
            while (tiles.MoveNext(out var tileRef))
            {
                if (!_glows.Glows.TryGetValue(tileRef.Tile.TypeId, out var glow))
                    continue;

                var (proto, texture) = glow;
                var color = Color.InterpolateBetween(proto.Dim, proto.Bright, Breath(proto, time, tileRef.GridIndices));
                var size = texture.Height;
                var region = UIBox2.FromDimensions(new Vector2(tileRef.Tile.Variant * size, 0), new Vector2(size, size));
                handle.DrawTextureRectRegion(texture, _lookup.GetLocalBounds(tileRef, grid.Comp.TileSize), color, region);
            }
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    private void DrawLight(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye || !_overlays.TryGetOverlay<BeforeLightTargetOverlay>(out var lightOverlay))
            return;

        var bounds = lightOverlay.EnlargedBounds;
        _grids.Clear();
        _map.FindGridsIntersecting(args.MapId, bounds, ref _grids, approx: true);
        if (_grids.Count == 0)
            return;

        var handle = args.WorldHandle;
        var viewport = args.Viewport;
        var target = lightOverlay.GetCachedForViewport(viewport).EnlargedLightTarget;
        var lightScale = viewport.LightRenderTarget.Size / (Vector2) viewport.Size;
        var scale = viewport.RenderScale / (Vector2.One / lightScale);
        var time = (float) _timing.RealTime.TotalSeconds;

        handle.RenderInRenderTarget(target,
            () =>
            {
                var invMatrix = target.GetWorldToLocalMatrix(eye, scale);
                handle.UseShader(_additive);
                foreach (var grid in _grids)
                {
                    handle.SetTransform(Matrix3x2.Multiply(_transform.GetWorldMatrix(grid.Owner), invMatrix));
                    var tiles = _map.GetTilesIntersecting(grid.Owner, grid, bounds);
                    while (tiles.MoveNext(out var tileRef))
                    {
                        if (!_glows.Glows.TryGetValue(tileRef.Tile.TypeId, out var glow))
                            continue;

                        var proto = glow.Proto;
                        var k = proto.LightFloor + (1f - proto.LightFloor) * Breath(proto, time, tileRef.GridIndices);
                        var light = new Color(proto.Light.R * k, proto.Light.G * k, proto.Light.B * k);
                        handle.DrawRect(_lookup.GetLocalBounds(tileRef, grid.Comp.TileSize), light);
                    }
                }

                handle.UseShader(null);
            }, null);

        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>Where this tile is in its breath, 0 to 1.</summary>
    private static float Breath(WFTileGlowPrototype proto, float time, Vector2i indices)
    {
        var phase = MathF.Tau * time / MathF.Max(proto.Period, 0.1f) + indices.X * proto.Wave.X + indices.Y * proto.Wave.Y;
        return 0.5f + 0.5f * MathF.Sin(phase);
    }
}
