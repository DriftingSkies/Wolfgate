using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.PlanetPois;

/// <summary>Appearance of an automated outpost's core.</summary>
[Serializable, NetSerializable]
public enum WFPoiCoreVisuals : byte
{
    Hacked,
}

/// <summary>Hacking an automated outpost's core.</summary>
[Serializable, NetSerializable]
public sealed partial class WFPoiCoreHackDoAfterEvent : SimpleDoAfterEvent;

/// <summary>A sweep with a survey scanner.</summary>
[Serializable, NetSerializable]
public sealed partial class WFPoiSurveyDoAfterEvent : SimpleDoAfterEvent;
