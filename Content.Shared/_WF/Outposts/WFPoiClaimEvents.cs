using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Outposts;

/// <summary>A multitool worked into a site's claim terminal, to hack it.</summary>
[Serializable, NetSerializable]
public sealed partial class WFPoiHackDoAfterEvent : SimpleDoAfterEvent;
