using Robust.Shared.Serialization;

namespace Content.Shared._WG.RMCCore.Movement;

[Serializable, NetSerializable]
public sealed class RMCNetCVarsEvent(int bufferSize) : EntityEventArgs
{
    public readonly int BufferSize = bufferSize;
}
