using System.Numerics;
using Content.Shared._WF.Remade.Limbs;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._WF.Remade.Limbs;

/// <summary>Thin writhing tentacles from a downed mender to each severed limb it is dragging back.</summary>
public sealed class WFReclaimTentacleOverlay : Overlay
{
    private const int Segments = 22;

    private static readonly Color Flesh = Color.FromHex("#3a1018");
    private static readonly Color Wet = Color.FromHex("#8a2a36");

    /// <summary>Each strand's phase, width and how far it wanders, so three twist round each other.</summary>
    private static readonly (float Phase, float Width, float Wander)[] Strands =
    {
        (0f, 0.075f, 0.16f),
        (2.1f, 0.055f, 0.2f),
        (4.2f, 0.045f, 0.12f),
    };

    private readonly IEntityManager _entities;
    private readonly IGameTiming _timing;
    private readonly SharedTransformSystem _transforms;
    private readonly Vector2[] _strip = new Vector2[(Segments + 1) * 2];
    private readonly Vector2[] _line = new Vector2[Segments + 1];

    /// <summary>True with reduced motion: the tentacles hold still.</summary>
    public bool Still;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WFReclaimTentacleOverlay(IEntityManager entities, IGameTiming timing)
    {
        _entities = entities;
        _timing = timing;
        _transforms = entities.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var time = Still ? 0f : (float) _timing.RealTime.TotalSeconds;
        var view = args.WorldBounds.Enlarged(8f);
        var query = _entities.EntityQueryEnumerator<WFReclaimTentacleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var tentacle, out var xform))
        {
            if (xform.MapID != args.MapId || !_entities.TryGetComponent(tentacle.Puller, out TransformComponent? pullerXform)
                || pullerXform.MapID != args.MapId)
            {
                continue;
            }

            var from = _transforms.GetWorldPosition(pullerXform);
            var to = _transforms.GetWorldPosition(xform);
            if (!view.Contains(from) && !view.Contains(to))
                continue;

            var seed = uid.Id * 0.37f;
            foreach (var (phase, width, wander) in Strands)
            {
                Trace(from, to, time, seed + phase, wander);
                Fill(handle, width, Flesh);
                Fill(handle, width * 0.35f, Wet);
            }
        }
    }

    /// <summary>Lays out one strand from the mender to the limb, swaying most in the middle and pinned at both ends.</summary>
    private void Trace(Vector2 from, Vector2 to, float time, float phase, float wander)
    {
        var along = to - from;
        var length = along.Length();
        var side = length > 0.001f ? new Vector2(-along.Y, along.X) / length : Vector2.UnitY;
        for (var i = 0; i <= Segments; i++)
        {
            var t = i / (float) Segments;
            var pinned = MathF.Sin(t * MathF.PI);
            var sway = MathF.Sin(t * length * 2.6f - time * 4.2f + phase) * wander * pinned
                       + MathF.Sin(t * 9f + time * 2.3f + phase * 1.7f) * wander * 0.35f * pinned;
            _line[i] = from + along * t + side * sway;
        }
    }

    /// <summary>Draws the traced strand as a ribbon that thins towards the limb.</summary>
    private void Fill(DrawingHandleWorld handle, float width, Color colour)
    {
        for (var i = 0; i <= Segments; i++)
        {
            var prev = _line[Math.Max(i - 1, 0)];
            var next = _line[Math.Min(i + 1, Segments)];
            var dir = next - prev;
            var normal = dir.LengthSquared() > 1e-6f ? Vector2.Normalize(new Vector2(-dir.Y, dir.X)) : Vector2.UnitY;
            var half = width * (1f - 0.65f * i / Segments) * 0.5f;
            _strip[i * 2] = _line[i] + normal * half;
            _strip[i * 2 + 1] = _line[i] - normal * half;
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _strip, colour);
    }
}
