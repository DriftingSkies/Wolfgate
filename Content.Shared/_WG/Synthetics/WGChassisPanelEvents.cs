using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Synthetics;

/// <summary>A mechanic's multitool linking into someone else's chassis panel.</summary>
[Serializable, NetSerializable]
public sealed partial class WGChassisPanelLinkDoAfterEvent : SimpleDoAfterEvent;
