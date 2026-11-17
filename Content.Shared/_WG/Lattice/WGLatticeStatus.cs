using Robust.Shared.Serialization;

namespace Content.Shared._WG.Lattice;

/// <summary>The Lattice status console's window; it reads the field component, so it needs no state of its own.</summary>
[Serializable, NetSerializable]
public enum WGLatticeStatusUiKey : byte
{
    Key,
}
