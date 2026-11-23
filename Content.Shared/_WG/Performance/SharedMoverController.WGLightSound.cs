using Content.Shared._WG.Performance;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    [Dependency] private SharedWGLightSoundSystem _wgLightSound = default!;
}
