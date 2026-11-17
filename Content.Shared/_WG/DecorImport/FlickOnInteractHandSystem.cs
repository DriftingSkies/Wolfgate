using Content.Shared._WG.RMCCore.Animations;
using Content.Shared.Interaction;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._WG.DecorImport;

/// <summary>
/// Plays the flick animation and sound of <see cref="FlickOnInteractHandComponent"/> on an empty-hand hit.
/// </summary>
public sealed partial class FlickOnInteractHandSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedRMCAnimationSystem _animation = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FlickOnInteractHandComponent, InteractHandEvent>(OnInteractHand);
    }

    private void OnInteractHand(Entity<FlickOnInteractHandComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _animation.Flick(ent.Owner, ent.Comp.AnimationState, ent.Comp.DefaultState);
        _audio.PlayPredicted(ent.Comp.Sound, ent, args.User);
    }
}
