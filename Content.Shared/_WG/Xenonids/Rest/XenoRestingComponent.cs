using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids.Rest;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoRestSystem))]
public sealed partial class XenoRestingComponent : Component;
