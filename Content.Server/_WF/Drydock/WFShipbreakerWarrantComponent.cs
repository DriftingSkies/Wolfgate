using Robust.Shared.Network;

namespace Content.Server._WF.Drydock;

/// <summary>
/// On someone wanted for breaking lawful property. Players who hurt them in person share the reward when they die.
/// </summary>
[RegisterComponent]
public sealed partial class WFShipbreakerWarrantComponent : Component
{
    /// <summary>The warrant's bounty board entry, or null when the board was not up.</summary>
    [ViewVariables]
    public uint? Contract;

    /// <summary>What the warrant pays; each further break adds to it.</summary>
    [ViewVariables]
    public int Reward;

    /// <summary>Players who hurt the wanted in person.</summary>
    [ViewVariables]
    public HashSet<NetUserId> Fighters = new();
}
