using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Rerolls the projectile of <see cref="FaunaRandomAmmoComponent"/> mobs after each shot.
/// </summary>
public sealed partial class FaunaRandomAmmoSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaRandomAmmoComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FaunaRandomAmmoComponent, GunShotEvent>(OnGunShot);
    }

    private void OnMapInit(Entity<FaunaRandomAmmoComponent> ent, ref MapInitEvent args)
    {
        Reroll(ent);
    }

    private void OnGunShot(Entity<FaunaRandomAmmoComponent> ent, ref GunShotEvent args)
    {
        Reroll(ent);
    }

    private void Reroll(Entity<FaunaRandomAmmoComponent> ent)
    {
        if (ent.Comp.Prototypes.Count == 0 || !TryComp<BasicEntityAmmoProviderComponent>(ent, out var ammo))
            return;

        ammo.Proto = _random.Pick(ent.Comp.Prototypes);
    }
}
