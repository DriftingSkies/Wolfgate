using Robust.Shared.Serialization;

namespace Content.Shared._WG.Xenonids.Rest;

[Serializable, NetSerializable]
public enum XenoRestState : byte
{
    NotResting,
    Resting
}
