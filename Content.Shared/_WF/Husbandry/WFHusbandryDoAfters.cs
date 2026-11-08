using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Husbandry;

/// <summary>A lasso thrown over an animal, one loop of it.</summary>
[Serializable, NetSerializable]
public sealed partial class WFLassoDoAfterEvent : SimpleDoAfterEvent;

/// <summary>An animal fed by hand towards taming it.</summary>
[Serializable, NetSerializable]
public sealed partial class WFTameFeedDoAfterEvent : SimpleDoAfterEvent;
