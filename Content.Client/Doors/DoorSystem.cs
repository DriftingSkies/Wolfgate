using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Client.Doors;

public sealed partial class DoorSystem : SharedDoorSystem
{
    [Dependency] private AnimationPlayerSystem _animationSystem = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoorComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<DoorComponent, AnimationCompletedEvent>(OnAnimationCompleted); // WOLFGATE(InteractionParticles)
    }

    // WOLFGATE(InteractionParticles) START: settle on the final sprite once the open/close animation finishes
    private void OnAnimationCompleted(Entity<DoorComponent> entity, ref AnimationCompletedEvent args)
    {
        if (args.Key != DoorComponent.OpenKey && args.Key != DoorComponent.CloseKey)
            return;

        if (!TryComp<SpriteComponent>(entity, out var sprite))
            return;

        switch (entity.Comp.State)
        {
            case DoorState.Open:
                foreach (var (layer, layerState) in entity.Comp.OpenSpriteStates)
                {
                    _sprite.LayerSetAutoAnimated((entity.Owner, sprite), layer, true);
                    _sprite.LayerSetRsiState((entity.Owner, sprite), layer, layerState);
                }

                break;
            case DoorState.Closed:
                foreach (var (layer, layerState) in entity.Comp.ClosedSpriteStates)
                {
                    _sprite.LayerSetAutoAnimated((entity.Owner, sprite), layer, true);
                    _sprite.LayerSetRsiState((entity.Owner, sprite), layer, layerState);
                }

                break;
        }
    }
    // WOLFGATE END

    protected override void OnComponentInit(Entity<DoorComponent> ent, ref ComponentInit args)
    {
        var comp = ent.Comp;
        comp.OpenSpriteStates = new List<(Enum, string)>(2);
        comp.ClosedSpriteStates = new List<(Enum, string)>(2);

        comp.OpenSpriteStates.Add((DoorVisualLayers.Base, comp.OpenSpriteState));
        comp.ClosedSpriteStates.Add((DoorVisualLayers.Base, comp.ClosedSpriteState));

        comp.OpeningAnimation = new Animation
        {
            Length = TimeSpan.FromSeconds(comp.OpeningAnimationTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = DoorVisualLayers.Base,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(comp.OpeningSpriteState, 0f),
                    },
                },
            },
        };

        comp.ClosingAnimation = new Animation
        {
            Length = TimeSpan.FromSeconds(comp.ClosingAnimationTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = DoorVisualLayers.Base,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(comp.ClosingSpriteState, 0f),
                    },
                },
            },
        };

        comp.EmaggingAnimation = new Animation
        {
            Length = TimeSpan.FromSeconds(comp.EmaggingAnimationTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = DoorVisualLayers.BaseUnlit,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(comp.EmaggingSpriteState, 0f),
                    },
                },
            },
        };
    }

    private void OnAppearanceChange(Entity<DoorComponent> entity, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!AppearanceSystem.TryGetData<DoorState>(entity, DoorVisuals.State, out var state, args.Component))
            state = DoorState.Closed;

        if (AppearanceSystem.TryGetData<string>(entity, DoorVisuals.BaseRSI, out var baseRsi, args.Component))
            UpdateSpriteLayers((entity.Owner, args.Sprite), baseRsi);

        // WOLFGATE(InteractionParticles) START: only stop animations that belong to another state
        //if (_animationSystem.HasRunningAnimation(entity, DoorComponent.AnimationKey))
        //    _animationSystem.Stop(entity.Owner, DoorComponent.AnimationKey);
        StopOtherDoorAnimations(entity, state);
        // WOLFGATE END

        UpdateAppearanceForDoorState(entity, args.Sprite, state);
    }

    // WOLFGATE(InteractionParticles) START: a door that reverses mid-animation must not run both animations at once
    private static readonly string[] WfDoorAnimationKeys =
    {
        DoorComponent.OpenKey, DoorComponent.CloseKey, DoorComponent.DenyKey, DoorComponent.EmagKey,
    };

    private void StopOtherDoorAnimations(EntityUid door, DoorState state)
    {
        var keep = state switch
        {
            DoorState.Open or DoorState.Opening => DoorComponent.OpenKey,
            DoorState.Closed or DoorState.Closing => DoorComponent.CloseKey,
            DoorState.Denying => DoorComponent.DenyKey,
            DoorState.Emagging => DoorComponent.EmagKey,
            _ => null,
        };

        foreach (var key in WfDoorAnimationKeys)
        {
            if (key != keep && _animationSystem.HasRunningAnimation(door, key))
                _animationSystem.Stop(door, key);
        }
    }
    // WOLFGATE END

    private void UpdateAppearanceForDoorState(Entity<DoorComponent> entity, SpriteComponent sprite, DoorState state)
    {
        _sprite.SetDrawDepth((entity.Owner, sprite), state is DoorState.Open ? entity.Comp.OpenDrawDepth : entity.Comp.ClosedDrawDepth);

        switch (state)
        {
            case DoorState.Open:
                if (_animationSystem.HasRunningAnimation(entity, DoorComponent.OpenKey)) // WOLFGATE(InteractionParticles)
                    return; // WOLFGATE(InteractionParticles)

                foreach (var (layer, layerState) in entity.Comp.OpenSpriteStates)
                {
                    // Allow animations to play while it's open (e.g., pinion);
                    // the animation unsets this so we gotta set it again.
                    _sprite.LayerSetAutoAnimated((entity.Owner, sprite), layer, true);
                    _sprite.LayerSetRsiState((entity.Owner, sprite), layer, layerState);
                }

                return;
            case DoorState.Closed:
                if (_animationSystem.HasRunningAnimation(entity, DoorComponent.CloseKey)) // WOLFGATE(InteractionParticles)
                    return; // WOLFGATE(InteractionParticles)

                foreach (var (layer, layerState) in entity.Comp.ClosedSpriteStates)
                {
                    _sprite.LayerSetAutoAnimated((entity.Owner, sprite), layer, true);
                    _sprite.LayerSetRsiState((entity.Owner, sprite), layer, layerState);
                }

                return;
            case DoorState.Opening:
                if (entity.Comp.OpeningAnimationTime == 0.0)
                    return;

                if (_animationSystem.HasRunningAnimation(entity, DoorComponent.OpenKey)) // WOLFGATE(InteractionParticles)
                    return; // WOLFGATE(InteractionParticles)

                _animationSystem.Play(entity, (Animation)entity.Comp.OpeningAnimation, DoorComponent.OpenKey); // WOLFGATE(InteractionParticles)

                return;
            case DoorState.Closing:
                if (entity.Comp.ClosingAnimationTime == 0.0 || entity.Comp.CurrentlyCrushing.Count != 0)
                    return;

                if (_animationSystem.HasRunningAnimation(entity, DoorComponent.CloseKey)) // WOLFGATE(InteractionParticles)
                    return; // WOLFGATE(InteractionParticles)

                _animationSystem.Play(entity, (Animation)entity.Comp.ClosingAnimation, DoorComponent.CloseKey); // WOLFGATE(InteractionParticles)

                return;
            case DoorState.Denying:
                if (_animationSystem.HasRunningAnimation(entity, DoorComponent.DenyKey)) // WOLFGATE(InteractionParticles)
                    return; // WOLFGATE(InteractionParticles)

                _animationSystem.Play(entity, (Animation)entity.Comp.DenyingAnimation, DoorComponent.DenyKey); // WOLFGATE(InteractionParticles)

                return;
            case DoorState.Emagging:
                if (_animationSystem.HasRunningAnimation(entity, DoorComponent.EmagKey)) // WOLFGATE(InteractionParticles)
                    return; // WOLFGATE(InteractionParticles)

                _animationSystem.Play(entity, (Animation)entity.Comp.EmaggingAnimation, DoorComponent.EmagKey); // WOLFGATE(InteractionParticles)

                return;
        }
    }

    private void UpdateSpriteLayers(Entity<SpriteComponent> sprite, string baseRsi)
    {
        if (!_resourceCache.TryGetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / baseRsi, out var res))
        {
            Log.Error("Unable to load RSI '{0}'. Trace:\n{1}", baseRsi, Environment.StackTrace);
            return;
        }

        _sprite.SetBaseRsi(sprite.AsNullable(), res.RSI);
    }
}
