using Content.Shared._WG.CombatConsole;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;

namespace Content.Server._WG.CombatConsole;

public sealed partial class WGCombatConsoleSystem
{
    /// <summary>Reads the firing provider's semantics and its existing ammunition-count event.</summary>
    public WGWeaponSupply GetWeaponSupply(EntityUid weapon)
    {
        var kind = GetSupplyKind(weapon);
        if (kind is WGWeaponSupplyKind.Unknown or WGWeaponSupplyKind.Infinite)
            return new WGWeaponSupply(kind, null, null);
        var ammunition = new GetAmmoCountEvent();
        RaiseLocalEvent(weapon, ref ammunition);
        return new WGWeaponSupply(kind, ammunition.Count, ammunition.Capacity);
    }

    private WGWeaponSupplyKind GetSupplyKind(EntityUid weapon)
    {
        if (TryComp<BasicEntityAmmoProviderComponent>(weapon, out var basic))
            return basic.Count == null ? WGWeaponSupplyKind.Infinite :
                HasComp<RechargeBasicEntityAmmoComponent>(weapon) ? WGWeaponSupplyKind.Recharging : WGWeaponSupplyKind.Finite;
        if (TryComp<BallisticAmmoProviderComponent>(weapon, out var ballistic))
            return ballistic.InfiniteUnspawned ? WGWeaponSupplyKind.Infinite : WGWeaponSupplyKind.Finite;
        if (HasComp<ProjectileBatteryAmmoProviderComponent>(weapon) || HasComp<HitscanBatteryAmmoProviderComponent>(weapon))
            return WGWeaponSupplyKind.Energy;
        if (HasComp<MagazineAmmoProviderComponent>(weapon) || HasComp<ChamberMagazineAmmoProviderComponent>(weapon))
        {
            return _containers.TryGetContainer(weapon, "gun_magazine", out var container) &&
                container is ContainerSlot { ContainedEntity: { } magazine }
                ? GetSupplyKind(magazine) : WGWeaponSupplyKind.Finite;
        }
        return HasComp<RevolverAmmoProviderComponent>(weapon) || HasComp<ContainerAmmoProviderComponent>(weapon) ||
            HasComp<SolutionAmmoProviderComponent>(weapon) ? WGWeaponSupplyKind.Finite : WGWeaponSupplyKind.Unknown;
    }
}
