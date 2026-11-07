using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MeridianTraits.Hemophage;

/// <summary>
/// The pulsating tumor that replaces a hemophage's heart. While it sits in a body, that body is a hemophage.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFHemophageTumorComponent : Component
{
    /// <summary>Organ slots the tumor corrupts in its host.</summary>
    [DataField]
    public HashSet<string> CorruptSlots = new() { "liver", "lungs", "stomach", "tongue" };

    /// <summary>How long a newly implanted organ takes to be corrupted.</summary>
    [DataField]
    public TimeSpan CorruptionTime = TimeSpan.FromSeconds(60);
}

[Serializable, NetSerializable]
public enum WFHemophageTumorVisuals : byte
{
    Dormant,
}
