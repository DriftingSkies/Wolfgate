using Robust.Shared.GameStates;

namespace Content.Shared._WG.Xenonids;

[RegisterComponent, NetworkedComponent]
[Access(typeof(XenoSystem))]
public sealed partial class XenoLightComponent : Component;