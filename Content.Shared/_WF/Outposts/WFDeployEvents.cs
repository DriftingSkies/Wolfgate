using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Outposts;

/// <summary>A packed outpost gizmo finished deploying on the tile in front of its user.</summary>
[Serializable, NetSerializable]
public sealed partial class WFDeployDoAfterEvent : SimpleDoAfterEvent;

/// <summary>A deployed outpost gizmo finished packing up.</summary>
[Serializable, NetSerializable]
public sealed partial class WFRepackDoAfterEvent : SimpleDoAfterEvent;
