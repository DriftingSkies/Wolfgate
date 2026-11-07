using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Utility;

namespace Content.Shared._WF.MeridianTraits.Hemophage;

/// <summary>
/// An organ taken over by a hemophage's tumor. A corrupted stomach and liver reject anything but blood, and a
/// corrupted organ in a body with no tumor slowly fails.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class WFTumorCorruptedComponent : Component
{
    /// <summary>Whether the corruption has finished; until then it is only taking root.</summary>
    [DataField, AutoNetworkedField]
    public bool Corrupted;

    /// <summary>When the corruption finishes.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan CorruptAt;

    /// <summary>The sprite a corrupted organ shows, or null to tint its own.</summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier.Rsi? Sprite;

    /// <summary>The tint for an organ with no corrupted sprite of its own.</summary>
    [DataField, AutoNetworkedField]
    public Color Tint = Color.FromHex("#333333");

    /// <summary>Share of the organ's health lost each second in a body with no tumor.</summary>
    [DataField]
    public float DecayPerSecond = 0.005f;
}
