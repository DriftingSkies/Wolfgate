using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Tugs;

/// <summary>Ends the do-after of strapping a crate to a trolley.</summary>
[Serializable, NetSerializable]
public sealed partial class WGTrolleyLoadDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Ends the do-after of taking a trolley's crate off.</summary>
[Serializable, NetSerializable]
public sealed partial class WGTrolleyUnloadDoAfterEvent : SimpleDoAfterEvent;
