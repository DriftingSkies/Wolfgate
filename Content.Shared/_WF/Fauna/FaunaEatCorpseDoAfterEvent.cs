using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Fauna;

/// <summary>
/// Raised when a fauna mob finishes eating or wrapping up a corpse.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class FaunaEatCorpseDoAfterEvent : SimpleDoAfterEvent;
