using Robust.Shared.GameStates;

namespace Content.Shared._WF.RMCCore.Stun;

[RegisterComponent, NetworkedComponent]
public sealed partial class RMCUnconsciousComponent : Component
{
    /// <summary>WOLFGATE: the knockout started the forced sleep, so it may end it; another source's sleep is left alone.</summary>
    [DataField]
    public bool SleepFromKnockout;
}
