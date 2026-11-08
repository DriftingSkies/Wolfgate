namespace Content.Server._WF.OutpostRaids;

/// <summary>
/// A humanoid raider. The director leaves its own HTN in place and marks what it carries as raid loot; a breacher
/// breaks into the outpost once at the claim edge, going for its turrets first.
/// </summary>
[RegisterComponent]
public sealed partial class WFRaidRaiderComponent : Component
{
    [DataField]
    public bool Breacher;
}
