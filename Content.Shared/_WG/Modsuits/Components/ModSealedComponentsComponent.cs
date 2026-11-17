using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Modsuits.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ModSealedComponentsComponent : Component
{
    [DataField(required: true)]
    public ComponentRegistry Components = new();
}
