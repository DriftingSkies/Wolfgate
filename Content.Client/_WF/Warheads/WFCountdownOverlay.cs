using System.Numerics;
using Content.Shared._WF.Warheads;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._WF.Warheads;

/// <summary>
/// Floats the seconds left over every counting warhead, in green pixel text like Skyrat's nuke countdown.
/// </summary>
public sealed partial class WFCountdownOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly SharedTransformSystem _transform;
    private readonly Font _font;

    /// <summary>Tiles above the entity's centre the number sits.</summary>
    private const float Lift = 0.75f;

    public WFCountdownOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transform = _entMan.System<SharedTransformSystem>();
        _font = new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/Minecraftia.ttf"), 12);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.ScreenHandle;
        var now = _timing.CurTime;
        var eyeRotation = _eye.CurrentEye.Rotation;

        var query = _entMan.EntityQueryEnumerator<WFCountdownDisplayComponent, TransformComponent>();
        while (query.MoveNext(out var display, out var xform))
        {
            if (display.End is not { } end || xform.MapID != args.MapId)
                continue;

            var world = _transform.GetWorldPosition(xform);
            if (!args.WorldAABB.Contains(world))
                continue;

            // Up the screen, whatever way the eye is turned.
            var lifted = world + (-eyeRotation).RotateVec(new Vector2(0f, Lift));
            var screen = _eye.WorldToScreen(lifted);

            var seconds = Math.Max(0, (int) Math.Ceiling((end - now).TotalSeconds));
            var text = seconds.ToString();
            var size = handle.GetDimensions(_font, text, 1f);
            var origin = screen - size / 2f;

            // A dark outline keeps it readable on any floor.
            foreach (var offset in Outline)
            {
                handle.DrawString(_font, origin + offset, text, Color.Black.WithAlpha(0.85f));
            }

            handle.DrawString(_font, origin, text, display.Color);
        }
    }

    private static readonly Vector2[] Outline =
    {
        new(-1f, 0f), new(1f, 0f), new(0f, -1f), new(0f, 1f),
    };
}
