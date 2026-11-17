using Robust.Shared.Serialization;

namespace Content.Shared._WG.Performance;

/// <summary>Every radar blip that went away during one server tick, in place of one event per blip.</summary>
[Serializable, NetSerializable]
public sealed class WGBlipRemovalBatchEvent(List<NetEntity> blips) : EntityEventArgs
{
    public readonly List<NetEntity> Blips = blips;
}
