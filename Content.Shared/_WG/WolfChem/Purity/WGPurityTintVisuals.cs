using Robust.Shared.Serialization;

namespace Content.Shared._WG.WolfChem.Purity;

/// <summary>Appearance key, true while a <c>WGPurityTint</c> entity's reagent is at or above its grade.</summary>
[Serializable, NetSerializable]
public enum WGPurityTintVisuals : byte
{
    Tinted,
}
