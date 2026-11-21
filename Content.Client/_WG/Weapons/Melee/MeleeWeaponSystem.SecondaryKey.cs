using Content.Shared.Weapons.Ranged.Components;

namespace Content.Client.Weapons.Melee;

public sealed partial class MeleeWeaponSystem
{
    /// <summary>
    /// Whether the weapon is also a gun fired with the secondary key, such as a ranged mob's own, so a melee alt
    /// attack on that key would put the weapon on cooldown and cancel the shot.
    /// </summary>
    public bool WGGunOwnsSecondaryKey(EntityUid weapon)
    {
        return TryComp<GunComponent>(weapon, out var gun) && !gun.UseKey;
    }
}
