using Content.Server.NPC.Queries.Considerations;

namespace Content.Server._WG.NpcCrew.HTN;

/// <summary>1 unless another crewman's plan already holds the target as its own; scored in NPCUtilitySystem.</summary>
public sealed partial class WGTargetUnclaimedCon : UtilityConsideration;
