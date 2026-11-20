using Content.Shared.EntityTable.EntitySelectors;

namespace Content.Server._WG.Augments;

/// <summary>
/// Spawns scavenged cyberware from a table, marked as worth what it sells back for like a piece bought in the
/// creator, then deletes itself.
/// </summary>
[RegisterComponent]
public sealed partial class WGAugmentScavengeSpawnerComponent : Component
{
    [DataField(required: true)]
    public EntityTableSelector Table = default!;
}
