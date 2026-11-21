using Robust.Shared.Serialization;

namespace Content.Shared._WG.Unstuck;

/// <summary>
/// Raised by a client that pressed Unstick me in the game menu.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGUnstuckRequestEvent : EntityEventArgs
{
}
