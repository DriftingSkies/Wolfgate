using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Marks a mob's spot on a demo map. Spawns the mob with its AI off at map init and respawns it when a
/// <see cref="FaunaDisplayButtonComponent"/> on the same map is pressed.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaDisplaySpawnerComponent : Component
{
    /// <summary>
    /// Mob spawned at this spot.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Prototype;

    /// <summary>
    /// The display mob currently spawned here.
    /// </summary>
    [ViewVariables]
    public EntityUid? Spawned;
}
