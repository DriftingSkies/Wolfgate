using System.Numerics;
using Content.Shared._WG.BorgSkins;
using Content.Shared.Buckle.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Client.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.BorgSkins;

/// <summary>Places oversized borg skins on their tile, plays their idle and walking animations and shows their poses.</summary>
public sealed partial class WGBorgSkinSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private AnimationPlayerSystem _animation = default!;

    private const string WreckState = "wreck";
    private const string TippedState = "tipped";
    // The key RotationVisualizerSystem plays its lying-down turn under.
    private const string RotateAnimation = "rotate";

    private const string MovingSuffix = "_moving";
    private const float MovingSpeedSquared = 0.04f;

    private static readonly Enum[] ChassisLayers =
    {
        BorgVisualLayers.Body,
        BorgVisualLayers.Light,
        BorgVisualLayers.LightStatus,
    };

    public override void Initialize()
    {
        base.Initialize();

        // Appearance handlers turn the eye and lamp layers back on; a pose has to hide them again before the frame is drawn.
        UpdatesAfter.Add(typeof(AppearanceSystem));

        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, AfterAutoHandleStateEvent>(OnHandleState);
    }

    private void OnStartup(Entity<BorgSwitchableSubtypeComponent> ent, ref ComponentStartup args)
    {
        ApplySkin(ent);
    }

    private void OnHandleState(Entity<BorgSwitchableSubtypeComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplySkin(ent);
    }

    private void ApplySkin(Entity<BorgSwitchableSubtypeComponent> ent)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        WGBorgSkinPrototype? skin = null;
        if (ent.Comp.BorgSubtype is { } subtype)
            _prototype.TryIndex<WGBorgSkinPrototype>(subtype.Id, out skin);

        var offset = skin?.Offset ?? Vector2.Zero;
        foreach (var key in ChassisLayers)
        {
            if (_sprite.TryGetLayer((ent.Owner, sprite), key, out var layer, false))
                _sprite.LayerSetOffset(layer, offset);
        }

        if (skin != null)
            EnsureComp<WGBorgSkinAnimatedComponent>(ent).Walks = skin.Walks;
        else
            RemComp<WGBorgSkinAnimatedComponent>(ent);
    }

    /// <summary>The pose state the borg should show: wrecked, seated or lying where it is buckled, tipped over, or the pose it chose.</summary>
    private string? WantedPose(EntityUid uid)
    {
        if (TryComp(uid, out MobStateComponent? mob) && mob.CurrentState is MobState.Critical or MobState.Dead)
            return WreckState;

        // A bed lays its occupant down, which would otherwise read as tipped over.
        if (TryComp(uid, out BuckleComponent? buckle) && buckle.BuckledTo is { } strap)
            return TryComp(strap, out StrapComponent? seat) && seat.Position == StrapPosition.Down ? "rest" : "sit";

        if (HasComp<StunnedComponent>(uid)
            || HasComp<KnockedDownComponent>(uid)
            || TryComp(uid, out StandingStateComponent? standing) && standing.CurrentState != StandingState.Standing)
            return TippedState;

        if (!TryComp(uid, out WGBorgPoseComponent? pose))
            return null;

        return pose.Pose switch
        {
            WGBorgPose.Rest => "rest",
            WGBorgPose.Sit => "sit",
            WGBorgPose.BellyUp => "bellyup",
            _ => null,
        };
    }

    /// <summary>Shows a pose state on the body alone, upright. False if the skin has no such state.</summary>
    private bool TryShowPose(Entity<SpriteComponent> ent, WGBorgSkinAnimatedComponent skin, string pose, double now)
    {
        if (!_sprite.TryGetLayer(ent.AsNullable(), BorgVisualLayers.Body, out var body, false)
            || body.ActualRsi is not { } rsi
            || !rsi.TryGetState(pose, out var state))
            return false;

        skin.Posed = true;
        if (body.State.Name != pose)
            _sprite.LayerSetRsiState(body, pose);

        if (state.IsAnimated)
            _sprite.LayerSetAnimationTime(body, (float)(now % state.TotalDelay));

        // The eyes and lamp are drawn for the standing body, and other systems keep turning them back on.
        foreach (var key in ChassisLayers)
        {
            if (!key.Equals(BorgVisualLayers.Body)
                && _sprite.TryGetLayer(ent.AsNullable(), key, out var layer, false)
                && layer.Visible)
                _sprite.LayerSetVisible(layer, false);
        }

        // A downed mob is drawn lying on its side; the pose art is already drawn that way.
        if (ent.Comp.Rotation != Angle.Zero)
        {
            if (TryComp(ent, out AnimationPlayerComponent? player) && _animation.HasRunningAnimation(player, RotateAnimation))
                _animation.Stop(player, RotateAnimation);
            _sprite.SetRotation(ent.AsNullable(), Angle.Zero);
        }

        return true;
    }

    /// <summary>Puts the standing body back and lets the stock visualizers restore the lights and rotation.</summary>
    private void EndPose(Entity<SpriteComponent> ent, WGBorgSkinAnimatedComponent skin)
    {
        skin.Posed = false;

        if (TryComp(ent, out BorgSwitchableTypeComponent? type)
            && _prototype.TryIndex(type.SelectedBorgType, out var proto)
            && _sprite.TryGetLayer(ent.AsNullable(), BorgVisualLayers.Body, out var body, false))
            _sprite.LayerSetRsiState(body, proto.SpriteBodyState);

        if (TryComp(ent, out AppearanceComponent? appearance))
            _appearance.QueueUpdate(ent, appearance);
    }

    public override void FrameUpdate(float frameTime)
    {
        var now = _timing.RealTime.TotalSeconds;
        var query = EntityQueryEnumerator<WGBorgSkinAnimatedComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var skin, out var sprite))
        {
            if (WantedPose(uid) is { } pose && TryShowPose((uid, sprite), skin, pose, now))
                continue;

            if (skin.Posed)
                EndPose((uid, sprite), skin);

            var moving = skin.Walks
                         && TryComp(uid, out PhysicsComponent? body)
                         && body.LinearVelocity.LengthSquared() > MovingSpeedSquared;

            foreach (var key in ChassisLayers)
            {
                if (!_sprite.TryGetLayer((uid, sprite), key, out var layer, false)
                    || layer.ActualRsi is not { } rsi
                    || layer.State.Name is not { } name)
                    continue;

                // Other systems set the idle state names, so the moving variant is re-applied whenever it is lost.
                if (skin.Walks && moving != name.EndsWith(MovingSuffix))
                {
                    var wanted = moving ? name + MovingSuffix : name[..^MovingSuffix.Length];
                    if (rsi.TryGetState(wanted, out _))
                        _sprite.LayerSetRsiState(layer, wanted);
                }

                // Hidden layers don't advance, so every layer is pinned to the clock to keep eyes and lamps on the body.
                if (rsi.TryGetState(layer.State, out var state) && state.IsAnimated)
                    _sprite.LayerSetAnimationTime(layer, (float)(now % state.TotalDelay));
            }
        }
    }
}
