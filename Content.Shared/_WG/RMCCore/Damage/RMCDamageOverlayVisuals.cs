using Robust.Shared.Serialization;

namespace Content.Shared._WG.RMCCore.Damage;

[Serializable, NetSerializable]
public enum RMCDamageOverlayVisuals : byte
{
    DamageOverlay,
    AdditionalDamageOverlay,
}
