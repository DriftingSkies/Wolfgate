using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Rest;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(XenoRestSystem))]
public sealed partial class ActionBlockIfRestingComponent : Component
{
    [DataField, AutoNetworkedField]
    public LocId Popup = "rmc-xeno-rest-cant";
}
