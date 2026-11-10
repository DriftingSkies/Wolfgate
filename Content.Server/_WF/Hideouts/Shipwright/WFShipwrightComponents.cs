namespace Content.Server._WF.Hideouts.Shipwright;

/// <summary>A ship built or registered at a shipwright bay: its guns and systems count against its refit points. Saved with it.</summary>
[RegisterComponent, Access(typeof(WFShipwrightSystem))]
public sealed partial class WFShipwrightShipComponent : Component
{
    /// <summary>The frame it was built from, or null for a hull built by hand.</summary>
    [DataField]
    public string? Frame;
}

/// <summary>A gun or system the shipwright fitted or certified, so it counts as legal; saved with it.</summary>
[RegisterComponent]
public sealed partial class WFShipwrightFittedComponent : Component;

/// <summary>A hardpoint a frame came with: it takes no refit points until it is swapped. Saved with it.</summary>
[RegisterComponent]
public sealed partial class WFShipwrightStockComponent : Component;

/// <summary>A hangar pad that is a shipwright bay: it takes shipwright ships, and its console builds and refits them.</summary>
[RegisterComponent]
public sealed partial class WFShipwrightBayComponent : Component;

/// <summary>The shipwright console: a drydock console whose cranes build frames and fit what its owner orders.</summary>
[RegisterComponent, Access(typeof(WFShipwrightSystem))]
public sealed partial class WFShipwrightConsoleComponent : Component
{
    /// <summary>
    /// Whether the console works a faction hangar's pad instead of a shipwright bay's: guns and strongboxes on a stock ship,
    /// within its stock firepower.
    /// </summary>
    [DataField]
    public bool Faction;

    /// <summary>How far, in metres, the console looks for its bay's pad.</summary>
    [DataField]
    public float BayRange = 24f;

    /// <summary>How far, in metres, the console looks for cranes to link when it is laid.</summary>
    [DataField]
    public float CraneRange = 24f;

    /// <summary>What the running job does once it finishes.</summary>
    [ViewVariables]
    public WFShipwrightJob Job;

    /// <summary>Who ordered the running job.</summary>
    [ViewVariables]
    public EntityUid? Orderer;
}

/// <summary>What a shipwright console's job is for, so its end is handled right.</summary>
public enum WFShipwrightJob : byte
{
    None,
    Frame,
    Refit,
    Certify,
}
