using System.Numerics;
using Content.Client._CE.ZLevels.Core;
using Content.Shared._WF.Waddle;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle;
using Content.Shared.Clothing.Components;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Jittering;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Waddle;

/// <summary>
/// Adds a step tilt and hop to the wearer of <see cref="WaddleWhenWornComponent"/> shoes while they hold a move key.
/// </summary>
public sealed partial class WaddleSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;

    private EntityQuery<JitteringComponent> _jitterQuery;
    private EntityQuery<InputMoverComponent> _moverQuery;
    private EntityQuery<SpriteComponent> _spriteQuery;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
        UpdatesAfter.Add(typeof(AppearanceSystem));
        UpdatesBefore.Add(typeof(CEClientZLevelsPostAnimSystem));

        _jitterQuery = GetEntityQuery<JitteringComponent>();
        _moverQuery = GetEntityQuery<InputMoverComponent>();
        _spriteQuery = GetEntityQuery<SpriteComponent>();

        SubscribeLocalEvent<WaddleWhenWornComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<WaddleWhenWornComponent> ent, ref ComponentShutdown args)
    {
        RemoveStep(ent.Comp);
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<WaddleWhenWornComponent, ClothingComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var waddle, out var clothing, out var xform))
        {
            var wearer = xform.ParentUid;
            if (clothing.InSlotFlag != SlotFlags.FEET
                || !_toggle.IsActivated(uid)
                || !_moverQuery.TryComp(wearer, out var mover)
                || !_spriteQuery.TryComp(wearer, out var sprite))
            {
                waddle.Wearer = null;
                waddle.Phase = 0;
                continue;
            }

            var buttons = SharedMoverController.GetNormalizedMovement(mover.HeldMoveButtons);
            if ((buttons & MoveButtons.AnyDirection) == MoveButtons.None || !CanWaddle(wearer, mover))
            {
                waddle.Wearer = null;
                waddle.Phase = 0;
                continue;
            }

            waddle.Wearer = wearer;

            var length = mover.Sprinting
                ? waddle.AnimationLength * waddle.RunAnimationLengthMultiplier
                : waddle.AnimationLength;

            waddle.Phase += frameTime / length;
            if (waddle.Phase >= 1)
            {
                waddle.Phase %= 1;
                waddle.TiltLeft = !waddle.TiltLeft;
            }

            // Rises from 0 to 1 and back over one step.
            var height = 1 - MathF.Abs(2 * waddle.Phase - 1);
            var tumble = waddle.TiltLeft ? -waddle.TumbleIntensity : waddle.TumbleIntensity;

            waddle.AppliedTilt = Angle.FromDegrees(tumble * height);
            waddle.AppliedHop = waddle.HopIntensity * height;
            waddle.WrittenRotation = sprite.Rotation + waddle.AppliedTilt;
            waddle.WrittenOffset = sprite.Offset + waddle.AppliedHop;
            _sprite.SetRotation((wearer, sprite), waddle.WrittenRotation);
            _sprite.SetOffset((wearer, sprite), waddle.WrittenOffset);
        }
    }

    /// <summary>
    /// Takes this frame's tilt and hop back off the wearer, unless another system has since set the sprite.
    /// </summary>
    public void RemoveStep(WaddleWhenWornComponent waddle)
    {
        if (waddle.Wearer is { } wearer && _spriteQuery.TryComp(wearer, out var sprite))
        {
            if (sprite.Rotation.Equals(waddle.WrittenRotation))
                _sprite.SetRotation((wearer, sprite), sprite.Rotation - waddle.AppliedTilt);

            if (sprite.Offset.Equals(waddle.WrittenOffset))
                _sprite.SetOffset((wearer, sprite), sprite.Offset - waddle.AppliedHop);
        }

        waddle.AppliedTilt = Angle.Zero;
        waddle.AppliedHop = Vector2.Zero;
    }

    private bool CanWaddle(EntityUid wearer, InputMoverComponent mover)
    {
        return _actionBlocker.CanMove(wearer, mover)
            && !_standing.IsDown(wearer)
            // CanMove stays true while buckled into a vehicle.
            && !_buckle.IsBuckled(wearer)
            && !_gravity.IsWeightless(wearer)
            && !_jitterQuery.HasComp(wearer);
    }
}
