namespace Content.Server._WF.Husbandry.Components;

/// <summary>An animal that becomes livestock once fed by hand enough times in a short while.</summary>
[RegisterComponent, Access(typeof(WFHusbandrySystem))]
public sealed partial class WFTameableComponent : Component
{
    /// <summary>Feedings it takes inside <see cref="Window"/>.</summary>
    [DataField]
    public int Feeds = 3;

    [DataField]
    public TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>How long one feeding takes.</summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(2);

    [DataField]
    public bool Tamed;

    /// <summary>When it was last fed, inside the window.</summary>
    [ViewVariables]
    public List<TimeSpan> Fed = new();
}
