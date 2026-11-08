namespace Content.Server._WF.OutpostIndustry.Components;

/// <summary>Sells the power it draws: credit builds up while it runs and a builder cashes it out.</summary>
[RegisterComponent, Access(typeof(WFOutpostIndustrySystem))]
public sealed partial class WFPowerExporterComponent : Component
{
    /// <summary>Spesos earned a second at full draw.</summary>
    [DataField]
    public float Rate = 0.5f;

    /// <summary>Spesos earned and not yet cashed out. Saved with the outpost.</summary>
    [DataField]
    public double Credit;
}
