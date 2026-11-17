using Content.Shared._WG.NpcCrew;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>A map marker that spawns a crewman of a role at map init and is that crewman's post.</summary>
[RegisterComponent]
public sealed partial class WGCrewSpawnPointComponent : Component
{
    [DataField(required: true)]
    public ProtoId<WGCrewRolePrototype> Role;

    [DataField]
    public string Group = string.Empty;

    [ViewVariables]
    public EntityUid? Spawned;
}
