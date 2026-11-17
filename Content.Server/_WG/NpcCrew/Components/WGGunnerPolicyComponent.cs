using Content.Shared._WG.NpcCrew;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>A grid's standing rule for when its NPC gunners open fire. Without one they fire on any ship marked hostile.</summary>
[RegisterComponent]
public sealed partial class WGGunnerPolicyComponent : Component
{
    /// <summary>When the ship's gunners open fire.</summary>
    [DataField]
    public WGGunnerPolicy Policy = WGGunnerPolicy.Hostiles;
}
