using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Drydock;

/// <summary>Raised on a crane when the cut that mirrors it ends.</summary>
[Serializable, NetSerializable]
public sealed partial class WFDrydockCraneMirrorDoAfterEvent : SimpleDoAfterEvent;
