using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Weapons.Ranged.Components;

namespace Content.Shared.Weapons.Ranged.Systems;

/// <summary>
/// wizden's <see cref="BallisticAmmoInteractLoaderComponent"/>: clicking ammo with the gun loads one piece of it.
/// </summary>
public abstract partial class SharedGunSystem
{
    [Dependency] private SharedStackSystem _wfStack = default!;

    [SubscribeLocalEvent]
    private void OnBallisticAmmoLoad(Entity<BallisticAmmoInteractLoaderComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target || !_netManager.IsServer)
            return;

        if (!TryComp<BallisticAmmoProviderComponent>(ent, out var ammo) ||
            GetBallisticShots(ammo) >= ammo.Capacity ||
            _whitelistSystem.IsWhitelistFailOrNull(ammo.Whitelist, target))
            return;

        var inserted = target;
        if (TryComp<StackComponent>(target, out var stack) && stack.Count > 1)
        {
            if (_wfStack.Split(target, 1, Transform(target).Coordinates, stack) is not { } split)
                return;

            inserted = split;
        }

        ammo.Entities.Add(inserted);
        Containers.Insert(inserted, ammo.Container);
        Audio.PlayPvs(ammo.SoundInsert, ent);
        UpdateBallisticAppearance(ent, ammo);
        UpdateAmmoCount(ent);
        DirtyField(ent.Owner, ammo, nameof(BallisticAmmoProviderComponent.Entities));
        args.Handled = true;
    }
}
