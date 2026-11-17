using Robust.Shared.Serialization;

namespace Content.Shared._WG.Bonfires;

/// <summary>Appearance keys of a fuelled fire.</summary>
[Serializable, NetSerializable]
public enum WGBonfireVisuals : byte
{
    /// <summary>Whether the fire is lit.</summary>
    Lit,
}
