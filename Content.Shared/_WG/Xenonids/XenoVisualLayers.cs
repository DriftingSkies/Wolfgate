using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids;

[Serializable, NetSerializable]
public enum XenoVisualLayers : byte
{
    Base,
    Hide,
    Crest,
    Fortify,
    Ovipositor,
    Burrow
}
