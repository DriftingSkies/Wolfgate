using Content.Server._WG.AdsIncursion.Systems;

namespace Content.Server._WG.AdsIncursion.Components;

/// <summary>Game rule marker: while it runs, the ADS incursion is on.</summary>
[RegisterComponent, Access(typeof(WGAdsCampaignRuleSystem))]
public sealed partial class WGAdsCampaignRuleComponent : Component;
