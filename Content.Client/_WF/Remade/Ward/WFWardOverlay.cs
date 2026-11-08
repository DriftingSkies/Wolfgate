using System.Numerics;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Ward;
using Content.Shared._WF.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Remade.Ward;

/// <summary>
/// The Ward's field as a membrane under the ship. It borrows the ship shield's cached band along the hull and its impact
/// ripples, and draws them dark red through <c>ward.swsl</c>, which adds the veins and the slow pulse. A loose stone gives a
/// soft, breathing skin; a rooted one at the Crescendo gives a hard, bright one.
/// </summary>
public sealed class WFWardOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> WardShader = "WFRemadeWardField";

    private static readonly Color Dim = Color.FromHex("#3d070d");
    private static readonly Color Bright = Color.FromHex("#a3141a");
    private static readonly Color Ember = Color.FromHex("#ff4a24");
    private static readonly Color HotWhite = Color.FromHex("#ffd9bd");

    private readonly IEntityManager _entities;
    private readonly SharedTransformSystem _transforms;
    private readonly IGameTiming _timing;
    private readonly Dictionary<EntityUid, List<WFWardImpact>> _impacts;
    private readonly Dictionary<EntityUid, Membrane> _membranes = new();
    private readonly List<EntityUid> _expired = new();
    private readonly ShaderInstance _shader;

    /// <summary>True with reduced motion: the veins and the pulse hold still.</summary>
    public bool Still;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public WFWardOverlay(IEntityManager entities, IPrototypeManager prototypes, IGameTiming timing,
        Dictionary<EntityUid, List<WFWardImpact>> impacts)
    {
        _entities = entities;
        _transforms = entities.System<SharedTransformSystem>();
        _timing = timing;
        _impacts = impacts;
        _shader = prototypes.Index(WardShader).Instance();
        ZIndex = 8;
    }

    /// <summary>The membrane's dim, bright, hit and flash colours: the stone's red, or shades of the field's tint.</summary>
    public static (Color Dim, Color Bright, Color Ember, Color Hot) Palette(WFWardFieldComponent field)
    {
        if (field.Tint is not { } tint)
            return (Dim, Bright, Ember, HotWhite);

        var dim = new Color(tint.R * 0.3f, tint.G * 0.3f, tint.B * 0.3f);
        return (dim, tint, Color.InterpolateBetween(tint, Color.White, 0.35f), Color.InterpolateBetween(tint, Color.White, 0.8f));
    }

    /// <summary>How hard the field looks, from 0 for a loose skin to 1 for the rooted stone.</summary>
    public static float Hardness(WFWardFieldComponent field)
    {
        return field.Rooted ? 1f : Math.Clamp((int) field.Stage / 5f * 0.6f, 0f, 0.6f);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var pixelsPerTile = args.ViewportBounds.Width / MathF.Max(1f, args.WorldAABB.Width);
        var distant = pixelsPerTile < 16f;

        _expired.Clear();
        foreach (var uid in _membranes.Keys)
        {
            if (_entities.Deleted(uid))
                _expired.Add(uid);
        }

        foreach (var uid in _expired)
        {
            _membranes[uid].Shader?.Dispose();
            _membranes.Remove(uid);
        }

        var query = _entities.EntityQueryEnumerator<WFWardFieldComponent, WFShipShieldVisualsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var field, out var visuals, out var xform))
        {
            var elapsed = MathF.Max(0f, (float) (_timing.CurTime - visuals.TransitionStarted).TotalSeconds);
            var transition = elapsed < WFShipShieldTransitionEffects.Duration(visuals.Transition) ? visuals.Transition : WFShipShieldTransition.None;
            var ending = transition is WFShipShieldTransition.Collapsing or WFShipShieldTransition.Lowering;
            if (xform.MapID != args.MapId || visuals.Contours.Length == 0
                || !ending && (!_entities.TryGetComponent<PhysicsComponent>(uid, out var physics) || !physics.CanCollide))
            {
                continue;
            }

            var progress = WFShipShieldTransitionEffects.Progress(transition, elapsed);
            var fade = transition switch
            {
                WFShipShieldTransition.Forming => progress,
                WFShipShieldTransition.Collapsing or WFShipShieldTransition.Lowering => 1f - progress,
                _ => 1f,
            };

            if (!_membranes.TryGetValue(uid, out var membrane) || !WFShipShieldMesh.ContoursEqual(membrane.Contours, visuals.Contours))
            {
                membrane?.Shader?.Dispose();
                membrane = new Membrane(visuals.Contours);
                _membranes[uid] = membrane;
            }

            var anchor = xform;
            if (visuals.Grid is { } grid && _entities.TryGetComponent<TransformComponent>(grid, out var gridTransform))
                anchor = gridTransform;

            var matrix = _transforms.GetWorldMatrix(anchor);
            var bounds = membrane.Bounds;
            var a = Vector2.Transform(bounds.BottomLeft, matrix);
            var b = Vector2.Transform(bounds.TopRight, matrix);
            var c = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), matrix);
            var d = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), matrix);
            var world = new Box2(Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)), Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d)));
            if (!args.WorldAABB.Intersects(world))
                continue;

            var hardness = Hardness(field);
            handle.UseShader(membrane.Configure(_shader, hardness, Still));
            var surface = distant ? membrane.Distant ??= new Surface(visuals.Contours, true) : membrane.Detailed ??= new Surface(visuals.Contours, false);
            _impacts.TryGetValue(uid, out var impacts);
            var (dim, bright, ember, hot) = Palette(field);
            surface.Update(Color.InterpolateBetween(dim, bright, hardness), ember, hot, hardness, fade, impacts, _timing.CurTime, distant);
            handle.SetTransform(matrix);
            DrawBatches(handle, surface.Vertices);
        }

        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        foreach (var membrane in _membranes.Values)
        {
            membrane.Shader?.Dispose();
        }

        _membranes.Clear();
        base.DisposeBehavior();
    }

    private static void DrawBatches(DrawingHandleWorld handle, DrawVertexUV2DColor[] vertices)
    {
        for (var offset = 0; offset < vertices.Length; offset += 8190)
        {
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
                new ReadOnlySpan<DrawVertexUV2DColor>(vertices, offset, Math.Min(8190, vertices.Length - offset)));
        }
    }

    private sealed class Membrane
    {
        public readonly Vector2[][] Contours;
        public readonly Box2 Bounds;
        public Surface? Detailed;
        public Surface? Distant;
        public ShaderInstance? Shader;
        private float _hardness = float.NaN;
        private bool? _still;

        public Membrane(Vector2[][] contours)
        {
            Contours = contours;
            var bounds = new Box2();
            var started = false;
            foreach (var contour in contours)
            {
                foreach (var point in contour)
                {
                    bounds = started ? bounds.ExtendToContain(point) : new Box2(point, point);
                    started = true;
                }
            }

            Bounds = new Box2(bounds.BottomLeft - new Vector2(WFShipShieldMesh.InwardDepth), bounds.TopRight + new Vector2(WFShipShieldMesh.InwardDepth));
        }

        public ShaderInstance Configure(ShaderInstance source, float hardness, bool still)
        {
            Shader ??= source.Duplicate();
            if (_hardness != hardness)
            {
                Shader.SetParameter("hardness", hardness);
                _hardness = hardness;
            }

            if (_still != still)
            {
                Shader.SetParameter("motion", still ? 0f : 1f);
                _still = still;
            }

            return Shader;
        }
    }

    /// <summary>One level of detail of a field: the band mesh, a colour per sample, and the vertices drawn.</summary>
    private sealed class Surface
    {
        private readonly WFShipShieldMesh _mesh;
        private readonly Color[] _colors;
        public readonly DrawVertexUV2DColor[] Vertices;
        private Color _lastBase;
        private float _lastFade = float.NaN;
        private bool _hadImpacts;
        private TimeSpan _next;

        public Surface(Vector2[][] contours, bool distant)
        {
            _mesh = new WFShipShieldMesh(contours, distant);
            _colors = new Color[_mesh.Samples.Count];
            Vertices = new DrawVertexUV2DColor[_mesh.Triangles.Count];
        }

        public void Update(Color baseColor, Color ember, Color hot, float hardness, float fade, List<WFWardImpact>? impacts, TimeSpan time, bool distant)
        {
            var active = impacts is { Count: > 0 };
            if (!active && !_hadImpacts && _lastBase == baseColor && _lastFade == fade)
                return;

            if (active && _hadImpacts && _lastBase == baseColor && _lastFade == fade && time < _next)
                return;

            _next = time + TimeSpan.FromSeconds(distant ? 1f / 15f : 1f / 30f);
            _lastBase = baseColor;
            _lastFade = fade;
            _hadImpacts = active;

            var baseAlpha = 0.55f + 0.35f * hardness;
            for (var i = 0; i < _colors.Length; i++)
            {
                var hit = 0f;
                var flash = 0f;
                if (active && impacts != null)
                {
                    for (var n = Math.Max(0, impacts.Count - (distant ? 8 : 24)); n < impacts.Count; n++)
                    {
                        var impact = impacts[n];
                        var age = (float) (time - impact.Time).TotalSeconds;
                        var distance = Vector2.Distance(_mesh.Samples[i], impact.Position);
                        if (distant)
                            distance = MathF.Max(0f, distance - _mesh.ImpactSampleRadius);

                        hit += WFShipShieldEffects.Heat(distance, age, impact.Strength) * 0.8f
                               + WFShipShieldEffects.Wave(distance, age, impact.Strength)
                               + WFShipShieldEffects.WaveWake(distance, age, impact.Strength) * 0.5f;
                        flash += WFShipShieldEffects.Flash(distance, age, impact.Strength);
                    }
                }

                hit = Math.Clamp(hit, 0f, 1.5f);
                flash = Math.Clamp(flash, 0f, 1f);
                var tint = Color.InterpolateBetween(baseColor, ember, Math.Clamp(hit, 0f, 1f));
                tint = Color.InterpolateBetween(tint, hot, flash * 0.8f);
                _colors[i] = Color.FromSrgb(tint).WithAlpha(baseAlpha + hit * 0.55f + flash * 0.4f);
            }

            for (var i = 0; i < _mesh.Triangles.Count; i++)
            {
                var vertex = _mesh.Triangles[i];
                var color = WFShipShieldMesh.Interpolate(vertex, _colors);
                Vertices[i] = new DrawVertexUV2DColor(vertex.Position, vertex.Position,
                    color.WithAlpha(Math.Clamp(color.A * vertex.Alpha * 1.7f * fade, 0f, 0.92f)));
            }
        }
    }
}
