namespace Content.Server._WF.Outposts.Components;

/// <summary>A CO2 cracker: while powered, turns the carbon dioxide in the air around it into oxygen, up to a rate.</summary>
[RegisterComponent, Access(typeof(WFOutpostLifeSupportSystem))]
public sealed partial class WFCo2CrackerComponent : Component
{
    /// <summary>Most carbon dioxide it cracks a second, in moles.</summary>
    [DataField]
    public float Rate = 1f;
}
