using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenobiology;

/// <summary>Appearance keys of a slime heart.</summary>
[Serializable, NetSerializable]
public enum WGSlimeHeartVisuals : byte
{
    /// <summary>A <see cref="WGSlimeHeartState"/>.</summary>
    State,
}

/// <summary>What a slime heart is doing, shown by its sprite and glow.</summary>
[Serializable, NetSerializable]
public enum WGSlimeHeartState : byte
{
    Dormant,
    Beating,
    /// <summary>Beating and regrowing the hull this beat.</summary>
    Mending,
}

[Serializable, NetSerializable]
public enum WGSlimeHeartVisualLayers : byte
{
    Heart,
}
