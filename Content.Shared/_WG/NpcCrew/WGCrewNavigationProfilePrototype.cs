using Robust.Shared.Prototypes;

namespace Content.Shared._WG.NpcCrew;

/// <summary>A reusable navigation configuration for crew scenarios.</summary>
[Prototype("wgCrewNavigation")]
public sealed partial class WGCrewNavigationProfilePrototype : IPrototype
{
    /// <summary>Scenario profile identifier.</summary>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Localized name shown in the setup profile picker.</summary>
    [DataField(required: true)]
    public LocId Name;

    /// <summary>Defaults copied into each mission using this profile.</summary>
    [DataField]
    public WGCrewNavigationSettings Settings = new();
}
