using Content.Server._WG.ReaverCampaign.Systems;

namespace Content.Server._WG.ReaverCampaign.Components;

/// <summary>Game rule marker: while it runs, the Ashfall Reaver campaign is on.</summary>
[RegisterComponent, Access(typeof(WGReaverCampaignRuleSystem))]
public sealed partial class WGReaverCampaignRuleComponent : Component;
