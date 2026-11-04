using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._WF.Xenoborgs;

/// <summary>
/// Ticks <see cref="BallisticAmmoSelfRefillerComponent"/>; the refill itself lives in a <see cref="SharedGunSystem"/> partial.
/// </summary>
public sealed partial class BallisticAmmoSelfRefillerSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<BallisticAmmoProviderComponent, BallisticAmmoSelfRefillerComponent>();
        while (query.MoveNext(out var uid, out var ammo, out var refiller))
        {
            _gun.BallisticSelfRefillerUpdate((uid, ammo, refiller));
        }
    }
}
