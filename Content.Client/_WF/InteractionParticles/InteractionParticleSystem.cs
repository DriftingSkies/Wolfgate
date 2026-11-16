using System.Numerics;
using Content.Client._WF.InteractionParticles;
using Content.Shared._WF.InteractionParticles;
using Content.Shared.Interaction;
using Robust.Client.Graphics;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client._WF.InteractionParticles;

/// <summary>
/// Spawns the hand and pulling particles that show what characters are interacting with.
/// </summary>
/// <remarks>
/// Ported from Starlight's <c>StellarInteractionParticleSystem</c>, itself a port of Moff Station and Ephemeral Space.
/// </remarks>
public sealed partial class InteractionParticleSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private AnimationPlayerSystem _animation = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TransformSystem _xform = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    private InteractionParticleMode _interactionParticleMode;
    private const string AnimateKey = "particle-animation";

    private static readonly Dictionary<InteractionParticleType, EntProtoId> InteractionParticleIds = new ()
    {
        { InteractionParticleType.Use, "WFInteractionParticleUse" },
        { InteractionParticleType.Pull, "WFInteractionParticlePull" },
        { InteractionParticleType.InHand, "WFInteractionParticleUse" },
    };

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, InteractionParticleCVars.Mode, value => _interactionParticleMode = (InteractionParticleMode) value, true);

        SubscribeAllEvent<InteractionParticleEvent>(OnInteractionParticle);
    }

    private void OnInteractionParticle(InteractionParticleEvent ev)
    {
        if (_interactionParticleMode == InteractionParticleMode.None)
            return;

        var performer = GetEntity(ev.Performer);
        var used = GetEntity(ev.Used);
        var target = GetEntity(ev.Target);

        if (!Exists(performer) || !Exists(target))
            return;

        var actor = performer;
        var type = ev.Type;
        if (type == InteractionParticleType.Pull)
        {
            (performer, target) = (target, performer);
        }

        var performerXform = Transform(performer);
        var targetXform = Transform(target);
        if (performerXform.MapID == MapId.Nullspace || targetXform.MapID == MapId.Nullspace)
            return;

        // Something the actor holds, wears or carries gets the in-hand particle; the server normally decides this.
        if (type != InteractionParticleType.Pull && _interaction.IsPrivateInteraction(actor, target))
            type = InteractionParticleType.InHand;

        if (!ShouldShowParticle(type, actor))
            return;

        if (TryComp<InteractionParticleTrackerComponent>(performer, out var tracker))
        {
            if (_timing.CurTime < tracker.ExpireTime)
                return;

            RemComp<InteractionParticleTrackerComponent>(performer);
        }

        // NoRotation sprites draw their offset in eye space, so turn the world delta by the eye's rotation.
        var worldDelta = _xform.GetWorldPosition(targetXform) - _xform.GetWorldPosition(performerXform);
        var performerTargetDelta = _eye.CurrentEye.Rotation.RotateVec(worldDelta);
        var inHandDelta = new Vector2(0, 0.75f);
        var particle = Spawn(InteractionParticleIds[type], performerXform.Coordinates);

        if (type == InteractionParticleType.InHand)
        {
            used = target;
            _xform.SetParent(particle, performer);
        }

        if (used is { } usedEntity && Exists(usedEntity) && TryComp<SpriteComponent>(usedEntity, out var usedSprite))
        {
            _sprite.CopySprite((usedEntity, usedSprite), particle);
            _sprite.SetDrawDepth(particle, (int) Shared.DrawDepth.DrawDepth.Effects);
        }

        var sprite = Comp<SpriteComponent>(particle);
        sprite.NoRotation = true;
        var spriteColor = sprite.Color;
        var animation = type switch
        {
            InteractionParticleType.Use => GetUseAnimation(performerTargetDelta, spriteColor),
            InteractionParticleType.Pull => GetPullAnimation(performerTargetDelta, spriteColor),
            InteractionParticleType.InHand => GetUseAnimation(inHandDelta, spriteColor),
            _ => throw new ArgumentOutOfRangeException(nameof(ev), $"Interaction particle event has unknown particle type {type}"),
        };
        _animation.Play(particle, animation, AnimateKey);

        EnsureComp<InteractionParticleTrackerComponent>(performer).ExpireTime = _timing.CurTime + ev.Cooldown;
    }

    private Animation GetUseAnimation(Vector2 endOffset, Color color)
    {
        var startRotation = _random.NextAngle(Angle.FromDegrees(-40), Angle.FromDegrees(40));
        var endRotation = Angle.Zero;
        var startScale = new Vector2(0.3f, 0.3f);
        var endScale = new Vector2(1f, 1f);
        var rotationLength = TimeSpan.FromMilliseconds(600);

        var startOffset = new Vector2();
        var offsetLength = TimeSpan.FromMilliseconds(250);

        var startColor = color.WithAlpha(color.A * 0.9f);
        var endColor = color.WithAlpha(0f);
        var colorLength = rotationLength + offsetLength;

        return new Animation()
        {
            Length = colorLength,

            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Rotation),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startRotation, 0f),
                        new AnimationTrackProperty.KeyFrame(endRotation, (float) rotationLength.TotalSeconds, Easings.OutBack),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startScale, 0f),
                        new AnimationTrackProperty.KeyFrame(endScale, (float) rotationLength.TotalSeconds, Easings.OutBack),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startOffset, 0f),
                        new AnimationTrackProperty.KeyFrame(endOffset, (float) offsetLength.TotalSeconds, Easings.OutBack),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startColor, 0f),
                        new AnimationTrackProperty.KeyFrame(startColor, (float) rotationLength.TotalSeconds),
                        new AnimationTrackProperty.KeyFrame(endColor, (float) offsetLength.TotalSeconds, Easings.InOutCirc),
                    },
                },
            },
        };
    }

    private Animation GetPullAnimation(Vector2 endOffset, Color color)
    {
        var rotationLength = TimeSpan.FromMilliseconds(8f * (1000f / 12f));

        var startOffset = new Vector2();
        var offsetLength = TimeSpan.FromMilliseconds(4f * (1000f / 12f));

        var endColor = color.WithAlpha(0f);

        return new Animation
        {
            Length = rotationLength,

            AnimationTracks =
            {
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(startOffset, 0f),
                        new AnimationTrackProperty.KeyFrame(endOffset, (float) rotationLength.TotalSeconds, Easings.InOutCirc),
                    },
                },
                new AnimationTrackComponentProperty()
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Color),
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(color, 0f),
                        new AnimationTrackProperty.KeyFrame(color, (float) offsetLength.TotalSeconds),
                        new AnimationTrackProperty.KeyFrame(endColor, (float) (rotationLength - offsetLength).TotalSeconds, Easings.InOutCirc),
                    },
                },
            },
        };
    }

    /// <summary>
    /// Determines whether a particle of the specified type should be shown for the given actor.
    /// </summary>
    private bool ShouldShowParticle(InteractionParticleType type, EntityUid actor)
    {
        var isLocalActor =
            _player.LocalEntity is { } localPlayer &&
            localPlayer == actor;

        return _interactionParticleMode switch
        {
            InteractionParticleMode.All =>
                type != InteractionParticleType.InHand || isLocalActor,

            InteractionParticleMode.WithoutInHand =>
                type != InteractionParticleType.InHand,

            InteractionParticleMode.None => false,

            // Invalid manually entered CVar values fail closed.
            _ => false,
        };
    }
}
