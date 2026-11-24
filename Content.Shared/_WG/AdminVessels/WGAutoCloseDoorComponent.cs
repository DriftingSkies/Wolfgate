using Robust.Shared.GameStates;

namespace Content.Shared._WG.AdminVessels;

/// <summary>
/// A door with no airlock that still closes behind whoever went through, such as a holodoor: once open it shuts after
/// <see cref="Delay"/>, and tries again each second while something stands in it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGAutoCloseDoorComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(2);
}
