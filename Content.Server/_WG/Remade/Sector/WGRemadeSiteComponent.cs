using Content.Shared._WG.Remade.Sector;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Remade.Sector;

/// <summary>On the encounter entity of a site: which site it is and the Scion put aboard, once its hull is real.</summary>
[RegisterComponent]
public sealed partial class WGRemadeSiteComponent : Component
{
    [ViewVariables]
    public ProtoId<WGRemadeSitePrototype> Site;

    [ViewVariables]
    public bool Populated;

    [ViewVariables]
    public EntityUid? Scion;
}
