using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._WF.MappingMode;

/// <summary>
/// Draws the world tile grid every level shares: a faint line on every tile edge and a stronger one every 8 tiles.
/// Hidden when zoomed out too far for single tiles to read.
/// </summary>
public sealed partial class WFMappingWorldGridOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;

    private static readonly Color LineColor = Color.White.WithAlpha(0.08f);
    private static readonly Color MajorColor = Color.White.WithAlpha(0.2f);

    private const int Major = 8;
    private const float MaxSpan = 160f;

    private readonly WFMappingClientSystem _system;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public WFMappingWorldGridOverlay(WFMappingClientSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _system.InSession &&
               _player.LocalEntity is { } local &&
               _entities.GetComponent<TransformComponent>(local).MapID == args.MapId &&
               args.WorldAABB.Width <= MaxSpan && args.WorldAABB.Height <= MaxSpan;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var view = args.WorldAABB;
        var left = (int) MathF.Floor(view.Left);
        var right = (int) MathF.Ceiling(view.Right);
        var bottom = (int) MathF.Floor(view.Bottom);
        var top = (int) MathF.Ceiling(view.Top);

        for (var x = left; x <= right; x++)
        {
            handle.DrawLine(new Vector2(x, bottom), new Vector2(x, top), x % Major == 0 ? MajorColor : LineColor);
        }

        for (var y = bottom; y <= top; y++)
        {
            handle.DrawLine(new Vector2(left, y), new Vector2(right, y), y % Major == 0 ? MajorColor : LineColor);
        }
    }
}
