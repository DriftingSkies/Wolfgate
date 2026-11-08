using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Projectile that flies through mobs of its shooter's side instead of hitting them.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaFactionPassComponent : Component
{
    /// <summary>
    /// Passes through members of these factions. When empty, passes through the shooter's friends.
    /// </summary>
    [DataField]
    public List<ProtoId<NpcFactionPrototype>> Factions = new();
}
