namespace Content.Server._WF.OutpostIndustry.Components;

/// <summary>What an outpost's producers have made this hour, against the hourly cap. Round state, not saved.</summary>
[RegisterComponent, Access(typeof(WFOutpostIndustrySystem))]
public sealed partial class WFOutpostOutputComponent : Component
{
    /// <summary>When the current hour began.</summary>
    [ViewVariables]
    public TimeSpan HourStart;

    /// <summary>Value made since <see cref="HourStart"/>, in spesos.</summary>
    [ViewVariables]
    public double Produced;
}
