using Robust.Shared.Serialization;

namespace Content.Shared._WG.PlanetVents;

/// <summary>Appearance key for a breathing vent's phase.</summary>
[Serializable, NetSerializable]
public enum WGPlanetVentVisuals : byte
{
    Phase,
}

/// <summary>Where a breathing vent is in its cycle.</summary>
[Serializable, NetSerializable]
public enum WGPlanetVentPhase : byte
{
    Rest,
    Inhale,
    Exhale,
}
