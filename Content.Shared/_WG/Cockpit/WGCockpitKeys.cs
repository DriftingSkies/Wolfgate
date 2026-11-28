using Robust.Shared.Input;

namespace Content.Shared._WG.Cockpit;

/// <summary>The cockpit's hotkeys, bound in the pilot's shuttle input context.</summary>
[KeyFunctions]
public static class WGCockpitKeys
{
    public static readonly BoundKeyFunction WGCockpitWeaponGroup1 = "WGCockpitWeaponGroup1";
    public static readonly BoundKeyFunction WGCockpitWeaponGroup2 = "WGCockpitWeaponGroup2";
    public static readonly BoundKeyFunction WGCockpitWeaponGroup3 = "WGCockpitWeaponGroup3";
    public static readonly BoundKeyFunction WGCockpitWeaponGroup4 = "WGCockpitWeaponGroup4";

    /// <summary>The fire-group recall keys in slot order, one per weapon group.</summary>
    public static readonly BoundKeyFunction[] WeaponGroups =
    {
        WGCockpitWeaponGroup1, WGCockpitWeaponGroup2, WGCockpitWeaponGroup3, WGCockpitWeaponGroup4,
    };
}
