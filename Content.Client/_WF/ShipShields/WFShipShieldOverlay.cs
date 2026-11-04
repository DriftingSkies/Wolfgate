using System.Numerics;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Remade.Ward;
using Content.Shared._WF.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.ShipShields;

/// <summary>Draws cached hull fields with hex cells and bounded impact-color sampling.</summary>
public sealed class WFShipShieldOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> WFShipShieldShimmerProto = "WFShipShieldShimmer";

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transforms;
    private readonly IGameTiming _timing;
    private readonly Dictionary<EntityUid, List<WFShipShieldOverlaySystem.Impact>> _impacts;
    private readonly Dictionary<EntityUid, CachedField> _fields = new();
    private readonly List<EntityUid> _expired = new();
    private readonly ShaderInstance _shader;

    /// <summary>Draws unlit energy beneath ship sprites.</summary>
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    /// <summary>Binds the impact cache and reusable field meshes.</summary>
    public WFShipShieldOverlay(IEntityManager entities, IPrototypeManager prototypes, IGameTiming timing,
        Dictionary<EntityUid, List<WFShipShieldOverlaySystem.Impact>> impacts)
    {
        _entities = entities;
        _transforms = entities.System<SharedTransformSystem>();
        _timing = timing;
        _impacts = impacts;
        _shader = prototypes.Index<ShaderPrototype>(WFShipShieldShimmerProto).Instance();
        ZIndex = 8;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var pixelsPerTile = args.ViewportBounds.Width / MathF.Max(1f, args.WorldAABB.Width);
        var distant = pixelsPerTile < 16f;
        _expired.Clear();
        foreach (var uid in _fields.Keys)
        {
            if (_entities.Deleted(uid))
                _expired.Add(uid);
        }
        foreach (var uid in _expired)
        {
            _fields[uid].Shader?.Dispose();
            _fields.Remove(uid);
        }
        var query = _entities.EntityQueryEnumerator<WFShipShieldVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var shield, out var xform))
        {
            // The Remade module's Ward draws its own field.
            if (_entities.HasComponent<WFWardFieldComponent>(uid))
                continue;

            var elapsed = MathF.Max(0f, (float) (_timing.CurTime - shield.TransitionStarted).TotalSeconds);
            var transition = elapsed < WFShipShieldTransitionEffects.Duration(shield.Transition)
                ? shield.Transition : WFShipShieldTransition.None;
            var ending = transition is WFShipShieldTransition.Collapsing or WFShipShieldTransition.Lowering;
            if (xform.MapID != args.MapId || (!ending &&
                (!_entities.TryGetComponent<PhysicsComponent>(uid, out var physics) || !physics.CanCollide)))
                continue;
            var progress = WFShipShieldTransitionEffects.Progress(transition, elapsed);
            if (!_fields.TryGetValue(uid, out var cached) || !WFShipShieldMesh.ContoursEqual(cached.Contours, shield.Contours))
            {
                cached?.Shader?.Dispose();
                cached = new CachedField(shield.Contours);
                _fields[uid] = cached;
            }
            cached.Contours = shield.Contours;
            var anchor = xform;
            if (shield.Grid is { } grid && _entities.TryGetComponent<TransformComponent>(grid, out var gridTransform))
                anchor = gridTransform;
            var matrix = _transforms.GetWorldMatrix(anchor);
            var bounds = cached.Bounds;
            var a = Vector2.Transform(bounds.BottomLeft, matrix);
            var b = Vector2.Transform(bounds.TopRight, matrix);
            var c = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), matrix);
            var d = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), matrix);
            var worldBounds = new Box2(Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)),
                Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d)));
            if (!args.WorldAABB.Intersects(worldBounds))
                continue;
            _entities.TryGetComponent<WFShipShieldShuntComponent>(uid, out var shunt);
            handle.UseShader(cached.ConfigureShader(_shader, shunt, distant, transition, progress));
            var mesh = distant ? cached.Distant ??= new RenderMesh(cached.Contours, true)
                : cached.Detailed ??= new RenderMesh(cached.Contours, false);
            _impacts.TryGetValue(uid, out var impacts);
            var tint = _entities.TryGetComponent<ShipShieldVisualsComponent>(uid, out var generatorVisuals)
                ? WFShipShieldEffects.HealthColor(shield.Health, generatorVisuals.ShieldColor)
                : WFShipShieldEffects.HealthColor(shield.Health);
            mesh.UpdateColors(tint, shield.Health, impacts, _timing.CurTime, distant);
            handle.SetTransform(matrix);
            DrawBatches(handle, DrawPrimitiveTopology.TriangleList, mesh.Triangles);
            if (!distant && pixelsPerTile >= 12f)
                DrawBatches(handle, DrawPrimitiveTopology.LineList, mesh.HexLines);
        }
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        foreach (var field in _fields.Values)
            field.Shader?.Dispose();
        _fields.Clear();
        base.DisposeBehavior();
    }

    private static void DrawBatches(DrawingHandleWorld handle, DrawPrimitiveTopology topology, DrawVertexUV2DColor[] vertices)
    {
        for (var offset = 0; offset < vertices.Length; offset += 8190)
            handle.DrawPrimitives(topology, Texture.White,
                new ReadOnlySpan<DrawVertexUV2DColor>(vertices, offset, Math.Min(8190, vertices.Length - offset)));
    }

    private sealed class CachedField
    {
        public Vector2[][] Contours;
        public readonly Box2 Bounds;
        public RenderMesh? Detailed;
        public RenderMesh? Distant;
        public ShaderInstance? Shader;
        private Vector2 _shuntCenter;
        private float _shuntDirection;
        private float _shuntConcentration;
        private float _shuntArc;
        private bool? _distant;
        private WFShipShieldTransition? _transition;
        private float _transitionProgress = float.NaN;

        public CachedField(Vector2[][] contours)
        {
            Contours = contours;
            var initialized = false;
            var bounds = new Box2();
            foreach (var contour in contours)
            foreach (var point in contour)
            {
                if (!initialized)
                {
                    bounds = new Box2(point, point);
                    initialized = true;
                }
                bounds = bounds.ExtendToContain(point);
            }
            Bounds = new Box2(bounds.BottomLeft - new Vector2(WFShipShieldMesh.InwardDepth),
                bounds.TopRight + new Vector2(WFShipShieldMesh.InwardDepth));
        }

        public ShaderInstance ConfigureShader(ShaderInstance source, WFShipShieldShuntComponent? shunt, bool distant,
            WFShipShieldTransition transition, float progress)
        {
            Shader ??= source.Duplicate();
            var center = shunt?.Center ?? Bounds.Center;
            var direction = shunt?.DirectionRadians ?? 0f;
            var concentration = shunt?.Concentration ?? 0f;
            var arc = shunt?.ArcRadians ?? MathF.Tau;
            if (_distant == null || _shuntCenter != center || _shuntDirection != direction ||
                _shuntConcentration != concentration || _shuntArc != arc)
            {
                Shader.SetParameter("shuntCenter", center);
                Shader.SetParameter("shuntDirection", new Vector2(MathF.Cos(direction), MathF.Sin(direction)));
                Shader.SetParameter("shuntConcentration", concentration);
                Shader.SetParameter("shuntCosHalfArc", MathF.Cos(arc * 0.5f));
                Shader.SetParameter("shuntBoost", WFShipShieldShuntMath.PeakStrength(concentration, arc));
                _shuntCenter = center;
                _shuntDirection = direction;
                _shuntConcentration = concentration;
                _shuntArc = arc;
            }
            if (_distant != distant)
            {
                Shader.SetParameter("shimmerStrength", distant ? 0f : 1f);
                _distant = distant;
            }
            if (_transition != transition || _transitionProgress != progress)
            {
                Shader.SetParameter("transitionMode", (float) transition);
                Shader.SetParameter("transitionProgress", progress);
                if (_transition == null)
                {
                    Shader.SetParameter("fieldMin", Bounds.BottomLeft);
                    Shader.SetParameter("fieldSize", Bounds.Size);
                }
                _transition = transition;
                _transitionProgress = progress;
            }
            return Shader;
        }
    }

    private sealed class RenderMesh
    {
        private readonly WFShipShieldMesh _mesh;
        private readonly Color[] _colors;
        private readonly Color[] _hexColors;
        public readonly DrawVertexUV2DColor[] Triangles;
        public readonly DrawVertexUV2DColor[] HexLines;
        private Color? _lastTint;
        private float _lastIntegrity = float.NaN;
        private bool _hadImpacts;
        private TimeSpan _nextUpdate;

        public RenderMesh(Vector2[][] contours, bool distant)
        {
            _mesh = new WFShipShieldMesh(contours, distant);
            _colors = new Color[_mesh.Samples.Count];
            _hexColors = new Color[_mesh.Samples.Count];
            Triangles = new DrawVertexUV2DColor[_mesh.Triangles.Count];
            HexLines = new DrawVertexUV2DColor[_mesh.HexLines.Count];
        }

        public void UpdateColors(Color tint, float integrity, List<WFShipShieldOverlaySystem.Impact>? impacts, TimeSpan time, bool distant)
        {
            var active = impacts is { Count: > 0 };
            if (!active && !_hadImpacts && _lastTint == tint && _lastIntegrity == integrity)
                return;
            if (active && _hadImpacts && _lastTint == tint && _lastIntegrity == integrity && time < _nextUpdate)
                return;
            _nextUpdate = time + TimeSpan.FromSeconds(distant ? 1f / 15f : 1f / 30f);
            _lastTint = tint;
            _lastIntegrity = integrity;
            _hadImpacts = active;
            for (var i = 0; i < _colors.Length; i++)
            {
                var heat = 0f;
                var wave = 0f;
                var flash = 0f;
                var wake = 0f;
                if (active && impacts != null)
                {
                    for (var hit = Math.Max(0, impacts.Count - (distant ? 8 : 32)); hit < impacts.Count; hit++)
                    {
                        var impact = impacts[hit];
                        var age = (float) (time - impact.Time).TotalSeconds;
                        var distance = Vector2.Distance(_mesh.Samples[i], impact.Position);
                        if (distant)
                            distance = MathF.Max(0f, distance - _mesh.ImpactSampleRadius);
                        heat += WFShipShieldEffects.Heat(distance, age, impact.Strength);
                        wave += WFShipShieldEffects.Wave(distance, age, impact.Strength);
                        flash += WFShipShieldEffects.Flash(distance, age, impact.Strength);
                        wake += WFShipShieldEffects.WaveWake(distance, age, impact.Strength);
                    }
                }
                var appearance = WFShipShieldEffects.Appearance(tint, heat, wave, flash, integrity, wake);
                _colors[i] = appearance.SurfaceTint.WithAlpha(appearance.Surface);
                _hexColors[i] = appearance.Tint.WithAlpha(appearance.Hexes);
            }
            Fill(_mesh.Triangles, Triangles, _colors);
            Fill(_mesh.HexLines, HexLines, _hexColors);
        }

        private static void Fill(List<WFShipShieldMesh.Vertex> source, DrawVertexUV2DColor[] destination, Color[] colors)
        {
            for (var i = 0; i < source.Count; i++)
            {
                var vertex = source[i];
                var color = WFShipShieldMesh.Interpolate(vertex, colors);
                destination[i] = new DrawVertexUV2DColor(vertex.Position, vertex.Position,
                    color.WithAlpha(Math.Clamp(color.A * vertex.Alpha, 0f, 0.85f)));
            }
        }
    }
}
