using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Avali.Feathers;

/// <summary>Marks an entity as an Avali feather.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FeatherComponent : Component;

/// <summary>Appearance data used to tint feather sprites.</summary>
[Serializable, NetSerializable]
public enum FeatherVisuals : byte
{
    FeatherColor,
    BloodColor,
}

/// <summary>Sprite layers used by feather entities.</summary>
[Serializable, NetSerializable]
public enum FeatherVisualLayers : byte
{
    Feather,
    Blood,
}
