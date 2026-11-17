using Content.Shared._WG.NpcCrew;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew.Components;

/// <summary>The crew profile a ship was crewed with, on its grid, for the rules a profile sets between its own ships.</summary>
[RegisterComponent]
public sealed partial class WGCrewShipProfileComponent : Component
{
    [DataField]
    public ProtoId<WGCrewProfilePrototype> Profile;
}
