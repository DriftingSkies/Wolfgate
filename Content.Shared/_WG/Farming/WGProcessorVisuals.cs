using Robust.Shared.Serialization;

namespace Content.Shared._WG.Farming;

/// <summary>Appearance keys for processing machines.</summary>
[Serializable, NetSerializable]
public enum WGProcessorVisuals : byte
{
    /// <summary>Whether it is working on a batch right now.</summary>
    Working,
}
