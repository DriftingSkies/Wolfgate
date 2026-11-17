using Robust.Shared.Serialization;

namespace Content.Shared._WG.DoomArcade;

/// <summary>Opens the client-side Doom window on an arcade machine.</summary>
[Serializable, NetSerializable]
public enum DoomArcadeUiKey : byte
{
    Key,
}
