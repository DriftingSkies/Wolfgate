using Content.Server.Spawners.EntitySystems;
using Robust.Shared.Prototypes;

namespace Content.Server.Spawners.Components;

/// <summary>
/// When a <c>TimedDespawnComponent"</c> despawns, another one will be spawned in its place.
/// </summary>
[RegisterComponent, Access(typeof(SpawnOnDespawnSystem))]
public sealed partial class SpawnOnDespawnComponent : Component
{
    /// <summary>
    /// Entity prototype to spawn.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Prototype = string.Empty;

    /// <summary>
    /// Mono: how many extra entities to spawn after the first.
    /// </summary>
    // WOLFGATE: default to one spawn so prototypes without a count do not duplicate
    [DataField]
    public int Count = 0;
}
