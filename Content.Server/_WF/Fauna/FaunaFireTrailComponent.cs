using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Leaves a fire on each tile the mob walks off, and sets the ground around it alight when it dies.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaFireTrailComponent : Component
{
    /// <summary>
    /// Fire spawned on each tile the mob leaves and around its corpse.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Fire;

    /// <summary>
    /// Tiles around the corpse that catch fire on death.
    /// </summary>
    [DataField]
    public int DeathRadius = 2;
}
