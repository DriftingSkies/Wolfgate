using Robust.Shared.Map;

namespace Content.Server._WF.OutpostRaids;

/// <summary>A mob sent in a raid on an outpost; the director walks it in, and takes it away at dawn or past its leash.</summary>
[RegisterComponent, Access(typeof(WFRaidDirectorSystem))]
public sealed partial class WFRaidMobComponent : Component
{
    public EntityUid Outpost;

    public int Raid;

    /// <summary>The point on the outpost's claim edge it walks to first.</summary>
    public EntityCoordinates Edge;

    /// <summary>It has reached the claim edge and is going for the outpost.</summary>
    public bool ReachedEdge;
}
