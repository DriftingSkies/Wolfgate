using Robust.Shared.Audio;

namespace Content.Server._WF.Outposts.Components;

/// <summary>
/// A lathe that sounds like it is working while it prints: a start sound, its ambient loop for as long as its queue
/// runs, and an end sound when it stops. The loop is the entity's <c>AmbientSound</c>, left disabled.
/// </summary>
[RegisterComponent, Access(typeof(WFLatheRunningSoundSystem))]
public sealed partial class WFLatheRunningSoundComponent : Component
{
    [DataField]
    public SoundSpecifier? StartSound;

    [DataField]
    public SoundSpecifier? EndSound;
}
