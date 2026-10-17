using System.Numerics;
using Content.Client.Atmos.Overlays;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.EntitySystems;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.HeatHaze;

/// <summary>
/// Shimmers the world seen through hot air, from the tile temperatures the gas tile overlay already networks.
/// </summary>
/// <remarks>
/// Two passes per viewport. Below the world, each hot tile adds a soft blob of heat to a low resolution mask. Among the
/// entities, just above fire, the screen is redrawn through a rising noise field scaled by that mask, so floors, walls,
/// mobs and flames shimmer while ghosts and HUD overlays stay sharp.
/// </remarks>
public sealed partial class HeatHazeOverlay : Overlay
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entMan = default!;
    private SharedMapSystem _mapMan => _entMan.System<SharedMapSystem>();
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>Air temperature, in kelvin, the haze fades in from: the base species' heat damage threshold.</summary>
    public const float MinTemperature = 325f;

    /// <summary>Air temperature, in kelvin, the haze reaches full strength at.</summary>
    public const float FullTemperature = 800f;

    /// <summary>Peak displacement at full heat, in tiles, before <see cref="Strength"/>.</summary>
    private const float PeakDisplacement = 0.08f;

    /// <summary>Radius of the blob of heat each hot tile adds to the mask, in tiles.</summary>
    private const float BlobRadius = 1.5f;

    /// <summary>Blob peak for full heat, so the overlapping blobs of a uniformly hot area sum to about 1.</summary>
    private const float BlobWeight = 1f / (0.942f * BlobRadius * BlobRadius);

    /// <summary>Viewport pixels per mask texel along each axis.</summary>
    private const int MaskDivisor = 4;

    /// <summary>
    /// Tiles the noise origin wraps at. The shader's noise lattice repeats every 256 cells and its frequencies are
    /// multiples of 1/4 per tile, so this wrap never shows a seam.
    /// </summary>
    private const float NoisePeriod = 1024f;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld | OverlaySpace.WorldSpaceEntities;
    public override bool RequestScreenTexture => true;

    /// <summary>Multiplier on the displacement, from <c>wf.heat_haze.strength</c>.</summary>
    public float Strength = 1f;

    private readonly SharedTransformSystem _xform;
    private readonly EntityQuery<GasTileOverlayComponent> _overlayQuery;
    private readonly ShaderInstance _maskShader;
    private readonly ShaderInstance _hazeShader;
    private List<Entity<MapGridComponent>> _grids = new();

    private IRenderTexture? _mask;

    /// <summary>Viewport the mask was last drawn for, and the world area it has heat in.</summary>
    private IClydeViewport? _maskViewport;
    private Box2? _hotBounds;

    public HeatHazeOverlay()
    {
        IoCManager.InjectDependencies(this);

        _xform = _entMan.System<SharedTransformSystem>();
        _overlayQuery = _entMan.GetEntityQuery<GasTileOverlayComponent>();
        _maskShader = _proto.Index<ShaderPrototype>("WFHeatHazeMask").Instance();
        _hazeShader = _proto.Index<ShaderPrototype>("WFHeatHaze").Instance().Duplicate();

        // Right after the fire sprites, so flames shimmer and ghosts don't.
        ZIndex = GasTileOverlay.GasOverlayZIndex + 1;
    }

    /// <summary>
    /// Haze strength for an air temperature in kelvin: 0 up to <see cref="MinTemperature"/>, easing up to 1 at
    /// <see cref="FullTemperature"/>.
    /// </summary>
    public static float GetHeat(float temperature)
    {
        var t = Math.Clamp((temperature - MinTemperature) / (FullTemperature - MinTemperature), 0f, 1f);
        return t * (2f - t);
    }

    /// <summary>
    /// Haze strength for a networked tile temperature. Vacuum and tiles without air have none.
    /// </summary>
    public static float GetHeat(ThermalByte temperature)
    {
        return temperature.TryGetTemperature(out var kelvin, onVacuumReturnTcmb: false) ? GetHeat(kelvin) : 0f;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (args.Space == OverlaySpace.WorldSpaceBelowWorld)
        {
            // Drawn here rather than mid entity pass, where switching render targets would reset the stencil.
            _maskViewport = args.Viewport;
            _hotBounds = args.MapId == MapId.Nullspace || Strength <= 0f ? null : DrawMask(args);
            return false;
        }

        return args.Viewport == _maskViewport && _hotBounds is { } hot && hot.Intersects(args.WorldAABB);
    }

    /// <summary>
    /// Sums a blob of heat per hot tile in view into the mask, and returns the world area they cover.
    /// </summary>
    private Box2? DrawMask(in OverlayDrawArgs args)
    {
        var size = args.Viewport.Size;
        var needed = new Vector2i(
            (size.X + MaskDivisor - 1) / MaskDivisor,
            (size.Y + MaskDivisor - 1) / MaskDivisor);

        // Only ever grows, so viewports of different sizes drawn in the same frame share one texture. Each stretches
        // its own view over the whole of it.
        if (_mask == null || _mask.Size.X < needed.X || _mask.Size.Y < needed.Y)
        {
            var grown = Vector2i.ComponentMax(needed, _mask?.Size ?? Vector2i.Zero);
            _mask?.Dispose();
            _mask = _clyde.CreateRenderTarget(grown,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8),
                new TextureSampleParameters { Filter = true },
                nameof(HeatHazeOverlay));
        }

        var worldToMask = args.Viewport.GetWorldToLocalMatrix()
                          * Matrix3x2.CreateScale((float) _mask.Size.X / size.X, (float) _mask.Size.Y / size.Y);
        var worldBounds = args.WorldBounds;
        var handle = args.WorldHandle;
        Box2? hot = null;

        _grids.Clear();
        _mapMan.FindGridsIntersecting(args.MapId, args.WorldAABB.Enlarged(BlobRadius), ref _grids);

        handle.RenderInRenderTarget(_mask,
            () =>
            {
                handle.UseShader(_maskShader);

                foreach (var grid in _grids)
                {
                    if (!_overlayQuery.TryComp(grid, out var overlay) || overlay.Chunks.Count == 0)
                        continue;

                    var tileSize = (float) grid.Comp.TileSize;
                    var gridToWorld = _xform.GetWorldMatrix(grid);
                    var view = _xform.GetInvWorldMatrix(grid).TransformBox(worldBounds).Enlarged(BlobRadius * tileSize);
                    var minTile = new Vector2i((int) MathF.Floor(view.Left / tileSize), (int) MathF.Floor(view.Bottom / tileSize));
                    var maxTile = new Vector2i((int) MathF.Floor(view.Right / tileSize), (int) MathF.Floor(view.Top / tileSize));
                    var minChunk = SharedGasTileOverlaySystem.GetGasChunkIndices(minTile);
                    var maxChunk = SharedGasTileOverlaySystem.GetGasChunkIndices(maxTile);
                    var blobSize = new Vector2(2f * BlobRadius * tileSize);
                    Box2? gridHot = null;

                    handle.SetTransform(gridToWorld * worldToMask);

                    for (var x = minChunk.X; x <= maxChunk.X; x++)
                    {
                        for (var y = minChunk.Y; y <= maxChunk.Y; y++)
                        {
                            if (!overlay.Chunks.TryGetValue(new Vector2i(x, y), out var chunk))
                                continue;

                            var enumerator = new GasChunkEnumerator(chunk);

                            while (enumerator.MoveNext(out var gas))
                            {
                                var heat = GetHeat(gas.ByteGasTemperature);

                                if (heat <= 0f)
                                    continue;

                                var tile = chunk.Origin + new Vector2i(enumerator.X, enumerator.Y);

                                if (tile.X < minTile.X || tile.X > maxTile.X || tile.Y < minTile.Y || tile.Y > maxTile.Y)
                                    continue;

                                var blob = Box2.CenteredAround(((Vector2) tile + new Vector2(0.5f)) * tileSize, blobSize);

                                // The mask shader reads the heat from the alpha, which skips the sRGB conversion.
                                handle.DrawTextureRect(Texture.White, blob, new Color(1f, 1f, 1f, heat * BlobWeight));
                                gridHot = gridHot?.Union(blob) ?? blob;
                            }
                        }
                    }

                    if (gridHot is { } local)
                    {
                        var world = gridToWorld.TransformBox(local);
                        hot = hot?.Union(world) ?? world;
                    }
                }
            },
            // Opaque, so the add blend (source times its alpha, plus destination times its alpha) sums the blobs.
            new Color(0f, 0f, 0f, 1f));

        return hot;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null || _mask == null || _hotBounds is not { } hot)
            return;

        // Fragment space has its origin at the bottom left, viewport local space at the top left.
        var viewport = args.Viewport;
        var bottom = viewport.Size.Y;
        var origin = viewport.LocalToWorld(new Vector2(0f, bottom)).Position;
        var right = viewport.LocalToWorld(new Vector2(1f, bottom)).Position - origin;
        var up = viewport.LocalToWorld(new Vector2(0f, bottom - 1f)).Position - origin;
        var tilesPerPixel = right.Length();

        if (tilesPerPixel <= 0f)
            return;

        // Fragment (0, 0) in world tiles along the screen's axes, so the noise sticks to the world as the eye moves.
        var noiseOrigin = new Vector2(
            Wrap(Vector2.Dot(origin, right) / tilesPerPixel),
            Wrap(Vector2.Dot(origin, up) / up.Length()));

        _hazeShader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _hazeShader.SetParameter("heat_mask", _mask.Texture);
        _hazeShader.SetParameter("noise_origin", noiseOrigin);
        _hazeShader.SetParameter("tiles_per_pixel", tilesPerPixel);
        _hazeShader.SetParameter("strength", PeakDisplacement * Strength / tilesPerPixel);

        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_hazeShader);
        handle.DrawRect(hot.Intersect(args.WorldAABB), Color.White);
        handle.UseShader(null);
    }

    private static float Wrap(float value)
    {
        return value - MathF.Floor(value / NoisePeriod) * NoisePeriod;
    }

    protected override void DisposeBehavior()
    {
        _mask?.Dispose();
        _mask = null;
        base.DisposeBehavior();
    }
}
