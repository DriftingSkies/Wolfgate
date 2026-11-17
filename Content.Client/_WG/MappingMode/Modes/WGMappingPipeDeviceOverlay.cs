using System.Numerics;
using Content.Shared._WG.MappingTools;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WG.MappingMode.Modes;

/// <summary>
/// Pipe mode's hovered cell: the armed atmos device's icon turned to the facing, and a tick on the side the facing
/// points to. The server may turn a device to meet the pipes around it.
/// </summary>
public sealed partial class WGMappingPipeDeviceOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;

    private static readonly Color Tick = Color.FromHex("#ffd54f");

    private readonly WGMappingBuildModeSystem _modes;
    private readonly WGMappingPipeMode _mode;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WGMappingPipeDeviceOverlay(WGMappingBuildModeSystem modes, WGMappingPipeMode mode)
    {
        IoCManager.InjectDependencies(this);
        _modes = modes;
        _mode = mode;
        _transform = _entities.System<SharedTransformSystem>();
        _sprite = _entities.System<SpriteSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_modes.Active ||
            _modes.Mode != WGMappingBuildMode.Pipe ||
            _modes.PipePainting ||
            _mode.Pressing ||
            !_modes.TryGetGridCell(_modes.MouseMapPosition, out var grid, out var cell) ||
            _entities.GetComponent<TransformComponent>(grid).MapID != args.MapId)
        {
            return;
        }

        var handle = args.WorldHandle;
        handle.SetTransform(_transform.GetWorldMatrix(grid));

        var box = new Box2(cell.X, cell.Y, cell.X + 1, cell.Y + 1);
        if (_modes.PipeDevice is { } device)
        {
            var angle = MappingToolsPipes.QuarterTurns(MappingToolsPipes.TurnsToFace(_modes.PipeFacing));
            var texture = _sprite.GetPrototypeIcon(device).Default;
            handle.DrawTextureRect(texture, new Box2Rotated(box, angle, box.Center), Color.White.WithAlpha(0.75f));
        }

        var step = MappingToolsPipes.Step(Vector2i.Zero, _modes.PipeFacing);
        var centre = box.Center;
        var edge = centre + new Vector2(step.X, step.Y) * 0.45f;
        var across = new Vector2(step.Y, -step.X) * 0.12f;
        handle.DrawLine(centre, edge, Tick);
        handle.DrawLine(edge, edge - new Vector2(step.X, step.Y) * 0.15f + across, Tick);
        handle.DrawLine(edge, edge - new Vector2(step.X, step.Y) * 0.15f - across, Tick);

        handle.SetTransform(Matrix3x2.Identity);
    }
}
