using Robust.Shared.GameStates;

namespace Content.Shared._WG.RMCCore.Light;

[RegisterComponent, NetworkedComponent]
[Access(typeof(CMPoweredLightSystem))]
public sealed partial class PreventAttackLightOffComponent : Component;
