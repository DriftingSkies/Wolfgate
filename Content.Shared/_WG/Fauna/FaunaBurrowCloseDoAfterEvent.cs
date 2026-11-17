using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Fauna;

/// <summary>
/// Raised when someone finishes filling in a fauna burrow with a shovel.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class FaunaBurrowCloseDoAfterEvent : SimpleDoAfterEvent;
