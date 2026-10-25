using System.Numerics;
using Content.Client._WF.MappingMode.Modes;
using Content.Shared.DeviceNetwork.Components;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;

namespace Content.Client._WF.MappingMode.Link;

/// <summary>
/// Draws signal links (source to sink) and device lists (holder to device) as thin lines with small squares at the
/// ends: every one on the level with the Links layer, and the selected source's or holder's brighter in Link mode.
/// </summary>
public sealed partial class WFMappingLinkOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color SignalColor = Color.FromHex("#ffb300");
    private static readonly Color DeviceColor = Color.FromHex("#4fc3f7");
    private static readonly Color SelectedColor = Color.FromHex("#ffffff");
    private static readonly Color BoxColor = Color.FromHex("#4fc3f7");

    private const float EndSize = 0.22f;

    /// <summary>Width of a link line in tiles.</summary>
    private const float LineWidth = 0.08f;

    private readonly WFMappingLinkSystem _link;
    private readonly WFMappingBuildModeSystem _modes;
    private readonly SharedTransformSystem _transform;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFMappingLinkOverlay(WFMappingLinkSystem link, WFMappingBuildModeSystem modes)
    {
        IoCManager.InjectDependencies(this);
        _link = link;
        _modes = modes;
        _transform = _entities.System<SharedTransformSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var map = args.MapId;

        if (_link.ShowAll)
        {
            if (_entities.TryGetComponent(_link.SnapshotMap, out TransformComponent? mapXform) && mapXform.MapID == map)
            {
                foreach (var link in _link.Snapshot)
                {
                    if (_entities.TryGetEntity(link.Source, out var source) &&
                        _entities.TryGetEntity(link.Sink, out var sink))
                    {
                        Line(handle, map, source.Value, sink.Value, SignalColor.WithAlpha(0.6f));
                    }
                }
            }

            var lists = _entities.AllEntityQueryEnumerator<DeviceListComponent, TransformComponent>(); // All: session maps are paused
            while (lists.MoveNext(out var holder, out var list, out var xform))
            {
                if (xform.MapID != map)
                    continue;

                foreach (var device in list.Devices)
                {
                    Line(handle, map, holder, device, DeviceColor.WithAlpha(0.6f));
                }
            }
        }

        if (!_link.ModeActive)
            return;

        if (_link.Source is { } selectedSource && TryPosition(selectedSource, map, out var sourcePosition))
        {
            Outline(handle, sourcePosition, SignalColor);
            foreach (var sink in _link.SinksOf(selectedSource))
            {
                Line(handle, map, selectedSource, sink, SelectedColor);
            }
        }

        if (_link.Holder is { } selectedHolder && TryPosition(selectedHolder, map, out var holderPosition))
        {
            Outline(handle, holderPosition, DeviceColor);
            if (_entities.TryGetComponent(selectedHolder, out DeviceListComponent? list))
            {
                foreach (var device in list.Devices)
                {
                    Line(handle, map, selectedHolder, device, SelectedColor);
                }
            }
        }

        if (_modes.Handler is WFMappingLinkMode mode && mode.TryGetBox(out var a, out var b) && a.MapId == map)
        {
            var box = new Box2(Vector2.Min(a.Position, b.Position), Vector2.Max(a.Position, b.Position));
            handle.DrawRect(box, BoxColor.WithAlpha(0.15f));
            handle.DrawRect(box, BoxColor, filled: false);
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

    private void Line(DrawingHandleWorld handle, MapId map, EntityUid from, EntityUid to, Color color)
    {
        if (!TryPosition(from, map, out var a) || !TryPosition(to, map, out var b))
            return;

        // A quad rather than DrawLine: a one-pixel line vanishes over a busy grid at any zoom.
        var along = b - a;
        if (along.LengthSquared() > 0.0001f)
        {
            var side = Vector2.Normalize(new Vector2(-along.Y, along.X)) * (LineWidth / 2f);
            var quad = new[] { a + side, b + side, a - side, b - side };
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, quad, color);
        }

        handle.DrawRect(Box2.CenteredAround(a, new Vector2(EndSize)), color);
        handle.DrawRect(Box2.CenteredAround(b, new Vector2(EndSize)), color);
    }

    private static void Outline(DrawingHandleWorld handle, Vector2 position, Color color)
    {
        handle.DrawRect(Box2.CenteredAround(position, new Vector2(0.9f)), color, filled: false);
    }
}
