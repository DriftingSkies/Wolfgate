using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenobiology;

/// <summary>Appearance keys of a slime heart.</summary>
[Serializable, NetSerializable]
public enum WFSlimeHeartVisuals : byte
{
    /// <summary>A <see cref="WFSlimeHeartState"/>.</summary>
    State,
}

/// <summary>What a slime heart is doing, shown by its sprite and glow.</summary>
[Serializable, NetSerializable]
public enum WFSlimeHeartState : byte
{
    Dormant,
    Beating,
    /// <summary>Beating and regrowing the hull this beat.</summary>
    Mending,
}

[Serializable, NetSerializable]
public enum WFSlimeHeartVisualLayers : byte
{
    Heart,
}
