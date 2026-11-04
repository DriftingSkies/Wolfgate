using Robust.Shared.Serialization;

namespace Content.Shared._WF.Xenoborgs;

/// <summary>
/// Appearance key for a xenoborg extractor that is drawing power.
/// </summary>
[Serializable, NetSerializable]
public enum XenoborgExtractorVisuals : byte
{
    Consuming,
}
