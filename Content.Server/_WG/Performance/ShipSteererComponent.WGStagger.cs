using Content.Server.Physics.Controllers;

namespace Content.Server._Mono.NPC.HTN;

public sealed partial class ShipSteererComponent
{
    /// <summary>What the steering worked out last, reused on the ticks between its turns.</summary>
    [ViewVariables]
    public ShuttleInput? WGLastInput;

    /// <summary>What the last area search found: each entity, and whether it is a grid.</summary>
    [ViewVariables]
    public List<(EntityUid Uid, bool IsGrid)> WGNearby = new();

    /// <summary>Steering turns until the next area search for obstacles.</summary>
    [ViewVariables]
    public int WGScanWait;
}
