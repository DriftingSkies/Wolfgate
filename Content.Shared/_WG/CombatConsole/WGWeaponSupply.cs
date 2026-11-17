using Robust.Shared.Serialization;

namespace Content.Shared._WG.CombatConsole;

/// <summary>Identifies the actual ammunition provider rather than inferring depletion from stored rounds.</summary>
[Serializable, NetSerializable]
public enum WGWeaponSupplyKind : byte
{
    Unknown,
    Finite,
    Infinite,
    Recharging,
    Energy,
}

/// <summary>Authoritative available shots and capacity for a connected weapon.</summary>
[Serializable, NetSerializable]
public readonly record struct WGWeaponSupply(WGWeaponSupplyKind Kind, int? Count, int? Capacity);
