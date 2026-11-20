using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Client._WG.Waddle;
using Content.Shared._WG.PixelShift;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client._WG.PixelShift;

/// <summary>
/// Adds each mob's pixel shift and tilt to its sprite after animations have set it, so lunges, jitter and lying down
/// still play from the mob's own base and the tilt stacks on whatever rotation they leave. The shown values glide to
/// the set ones each frame. <see cref="WGPixelShiftRestoreSystem"/> takes them back off before the next frame.
/// </summary>
public sealed class WGPixelShiftVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>
    /// How quickly the shown shift and tilt close on the set ones, per second. The shown value trails a held key by
    /// 1/20 s at any speed, about one and a half server ticks, which also smooths over the per-tick steps.
    /// </summary>
    private const float GlideRate = 20f;

    /// <summary>Pixels or degrees from the set value at which the glide snaps onto it.</summary>
    private const float GlideSnap = 0.05f;

    /// <summary>
    /// How far, in meters or radians, the sprite may have drifted from what this system wrote and still count as
    /// untouched. Stacked effects that take themselves off by subtracting leave rounding behind.
    /// </summary>
    private const double WriteTolerance = 1e-5;

    private EntityQuery<WGPixelShiftComponent> _shiftQuery;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
        UpdatesAfter.Add(typeof(AppearanceSystem));
        // Waddle stacks on top of the shift, so it comes off first.
        UpdatesBefore.Add(typeof(WaddleSystem));
        UpdatesBefore.Add(typeof(CEClientZLevelsPostAnimSystem));

        _shiftQuery = GetEntityQuery<WGPixelShiftComponent>();

        SubscribeLocalEvent<WGPixelShiftComponent, ComponentShutdown>((uid, comp, _) => Remove((uid, comp)));
    }

    public override void FrameUpdate(float frameTime)
    {
        var step = 1f - MathF.Exp(-GlideRate * frameTime);
        var query = EntityQueryEnumerator<WGPixelShiftComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var shift, out var sprite))
        {
            shift.ShownOffset = Glide(shift.ShownOffset, (Vector2) shift.Offset, step);
            shift.ShownTilt = Glide(shift.ShownTilt, shift.Tilt, step);

            var offset = shift.ShownOffset / EyeManager.PixelsPerMeter;
            var tilt = Angle.FromDegrees(shift.ShownTilt);

            shift.AppliedOffset = offset;
            shift.AppliedTilt = tilt;
            if (offset == Vector2.Zero && tilt == Angle.Zero)
                continue;

            // Added to the rotation the lying-down, buckle or rotate animation left this frame, never replacing it.
            shift.WrittenOffset = sprite.Offset + offset;
            shift.WrittenRotation = sprite.Rotation + tilt;
            _sprite.SetOffset((uid, sprite), shift.WrittenOffset);
            _sprite.SetRotation((uid, sprite), shift.WrittenRotation);
        }
    }

    private static Vector2 Glide(Vector2 shown, Vector2 target, float step)
    {
        var next = shown + (target - shown) * step;
        return Vector2.DistanceSquared(next, target) < GlideSnap * GlideSnap ? target : next;
    }

    private static float Glide(float shown, float target, float step)
    {
        var next = shown + (target - shown) * step;
        return MathF.Abs(next - target) < GlideSnap ? target : next;
    }

    /// <summary>
    /// Takes this frame's shift and tilt back off, unless something has set the sprite since. A rotation set since,
    /// such as the rotate animation or lying down, is the new base and the tilt goes on top of it next frame.
    /// </summary>
    public void Remove(Entity<WGPixelShiftComponent> ent)
    {
        var shift = ent.Comp;
        if (shift.AppliedOffset == Vector2.Zero && shift.AppliedTilt == Angle.Zero)
            return;

        if (TryComp<SpriteComponent>(ent, out var sprite))
        {
            if (Untouched(sprite.Offset, shift.WrittenOffset))
                _sprite.SetOffset((ent, sprite), sprite.Offset - shift.AppliedOffset);

            if (Untouched(sprite.Rotation, shift.WrittenRotation))
                _sprite.SetRotation((ent, sprite), sprite.Rotation - shift.AppliedTilt);
        }

        shift.AppliedOffset = Vector2.Zero;
        shift.AppliedTilt = Angle.Zero;
    }

    private static bool Untouched(Vector2 current, Vector2 written)
    {
        return Math.Abs(current.X - written.X) < WriteTolerance && Math.Abs(current.Y - written.Y) < WriteTolerance;
    }

    private static bool Untouched(Angle current, Angle written)
    {
        return Math.Abs(current.Theta - written.Theta) < WriteTolerance;
    }

    /// <summary>
    /// The shift currently in a sprite's offset, for systems that record the offset as a base to return to.
    /// </summary>
    public Vector2 CurrentShift(EntityUid uid, SpriteComponent sprite)
    {
        return _shiftQuery.TryComp(uid, out var shift) && Untouched(sprite.Offset, shift.WrittenOffset)
            ? shift.AppliedOffset
            : Vector2.Zero;
    }

    /// <summary>
    /// A sprite's offset without this frame's pixel shift, so status icons and bars drawn over the mob stay put.
    /// </summary>
    public Vector2 UnshiftedOffset(EntityUid uid, SpriteComponent sprite)
    {
        return _shiftQuery.TryComp(uid, out var shift) ? sprite.Offset - shift.AppliedOffset : sprite.Offset;
    }
}
