using Content.Shared.Nutrition.Components;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.Augments;

/// <summary>
/// A nutriment pump in the torso: when its body is starving it feeds it a little, enough to stop the starving and
/// not enough to stop the hunger.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WGNutrimentPumpComponent : Component
{
    /// <summary>The pump feeds while hunger is at this level or under it.</summary>
    [DataField]
    public HungerThreshold Threshold = HungerThreshold.Starving;

    /// <summary>The hunger one feed gives back.</summary>
    [DataField]
    public float Amount = 15f;

    /// <summary>How often the pump looks at its body's hunger.</summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(5);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextCheck;
}
