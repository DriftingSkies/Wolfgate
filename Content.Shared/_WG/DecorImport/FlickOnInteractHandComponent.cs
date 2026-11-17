using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._WG.DecorImport;

/// <summary>
/// Plays a sprite state once and a sound when someone hits this with an empty hand.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FlickOnInteractHandComponent : Component
{
    /// <summary>
    /// State played once on a hit.
    /// </summary>
    [DataField(required: true)]
    public SpriteSpecifier.Rsi AnimationState = default!;

    /// <summary>
    /// State shown again when the animation ends.
    /// </summary>
    [DataField(required: true)]
    public SpriteSpecifier.Rsi DefaultState = default!;

    /// <summary>
    /// Sound played on a hit.
    /// </summary>
    [DataField]
    public SoundSpecifier? Sound;
}
