using Content.Server._WF.PlanetPois;

namespace Content.Server._WF.Outposts.Components;

/// <summary>A terminal in a point of interest that makes the site an outpost for whoever claims it.</summary>
[RegisterComponent]
public sealed partial class WFPoiClaimTerminalComponent : Component
{
    /// <summary>The ground the site is on.</summary>
    [ViewVariables]
    public EntityUid Ground;

    /// <summary>The site this terminal claims.</summary>
    public WFPlanetPoiSite? Site;

    /// <summary>Whether a multitool has been worked into it, for a site that is hacked to claim.</summary>
    [ViewVariables]
    public bool Hacked;

    [DataField]
    public TimeSpan HackDelay = TimeSpan.FromSeconds(10);

    [DataField]
    public string HackQuality = "Pulsing";

    /// <summary>Damage a terminal to be repaired starts with.</summary>
    [DataField]
    public float BrokenDamage = 60f;
}
