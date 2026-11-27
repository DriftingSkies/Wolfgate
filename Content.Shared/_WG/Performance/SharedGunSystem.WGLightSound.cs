using Content.Shared._WG.Performance;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    [Dependency] protected SharedWGLightSoundSystem WGLightSound = default!;
}
