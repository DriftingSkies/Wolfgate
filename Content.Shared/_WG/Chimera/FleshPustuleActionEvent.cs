using Content.Shared.Actions;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Chimera;

/// <summary>Action that plants a flesh pustule on the targeted tile.</summary>
public sealed partial class WGPlantFleshPustuleActionEvent : WorldTargetActionEvent
{
    [DataField]
    public EntProtoId Entity = "WGFleshPustule";
}

/// <summary>Appearance keys the flesh pustule writes.</summary>
[Serializable, NetSerializable]
public enum WGFleshPustuleVisuals : byte
{
    Bursted,
}

/// <summary>Sprite layers of the flesh pustule.</summary>
[Serializable, NetSerializable]
public enum WGFleshPustuleVisualLayers : byte
{
    Closed,
    Popped,
}
